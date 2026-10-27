using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

[assembly: AssemblyTitle("AirGlass")]
[assembly: AssemblyDescription("AirPlay screen receiver for Windows")]
[assembly: AssemblyProduct("AirGlass")]
[assembly: AssemblyCompany("Ali Syed")]
[assembly: AssemblyVersion("2.7.2.0")]
[assembly: AssemblyFileVersion("2.7.2.0")]

namespace AirGlass {
 [DataContract] public class Settings {
  [DataMember] public string Name = "AirGlass";
  [DataMember] public string Resolution = "1920x1080";
  [DataMember] public int Fps = 30;
  [DataMember] public bool Audio = true;
  [DataMember] public bool Fullscreen = false;
  [DataMember] public bool Software = false;
  [DataMember] public string OptimizeFor = "Latency";
  [DataMember] public bool SmoothLatency = true;
  [DataMember] public string DecoderMode = "Direct3D11";
  [OnDeserializing] void InitializeNewSettings(StreamingContext context) { OptimizeFor = "Latency"; SmoothLatency = true; DecoderMode = null; }
  [OnDeserialized] void MigrateDecoder(StreamingContext context) { if (DecoderMode == null) DecoderMode = Software ? "Software" : "Auto"; }
  public string DecoderChoice { get { return Software ? "Software" : DecoderMode; } }
  public static readonly string[] Resolutions = { "854x480", "1280x720", "1600x900", "1920x1080", "2560x1440", "3840x2160" };
  public void Validate() {
   if (String.IsNullOrWhiteSpace(Name) || Name.Length > 48 || Name.Any(Char.IsControl)) throw new ArgumentException("Use a receiver name with 1–48 characters and no control characters.");
   if (!Resolutions.Contains(Resolution)) throw new ArgumentException("Choose a supported resolution.");
   if (Fps != 15 && Fps != 30 && Fps != 60) throw new ArgumentException("Choose 15, 30 or 60 fps.");
   if (OptimizeFor != "Latency" && OptimizeFor != "Picture") throw new ArgumentException("Choose Best latency or Best picture.");
   if (DecoderMode != "Auto" && DecoderMode != "Nvidia" && DecoderMode != "Direct3D11" && DecoderMode != "Software") throw new ArgumentException("Choose an available video decoder option.");
  }
  public static Settings Load(string file) {
   if (!File.Exists(file)) return new Settings();
   using (var input = File.OpenRead(file)) {
    var result = (Settings)new DataContractSerializer(typeof(Settings)).ReadObject(input);
    result.Validate(); return result;
   }
  }
  public void Save(string file) {
   Validate(); Directory.CreateDirectory(Path.GetDirectoryName(file));
   var temp = file + ".tmp";
   using (var output = File.Create(temp)) new DataContractSerializer(typeof(Settings)).WriteObject(output, this);
   if (File.Exists(file)) File.Replace(temp, file, null); else File.Move(temp, file);
  }
 }

 public static class Native {
  [DllImport("iphlpapi.dll")] static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool sort, int family, int tableClass, uint reserved);
  public static bool HasListener(int pid) {
   int size = 0; GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 3, 0);
   IntPtr table = Marshal.AllocHGlobal(size);
   try {
    if (GetExtendedTcpTable(table, ref size, false, 2, 3, 0) != 0) return false;
    int count = Marshal.ReadInt32(table);
    for (int i = 0; i < count; i++) {
     int offset = 4 + i * 24;
     if (Marshal.ReadInt32(table, offset) == 2 && Marshal.ReadInt32(table, offset + 20) == pid) return true;
    }
    return false;
   } finally { Marshal.FreeHGlobal(table); }
  }
  [DllImport("kernel32.dll", SetLastError=true)] static extern IntPtr CreateJobObject(IntPtr attrs, string name);
  [DllImport("kernel32.dll", SetLastError=true)] static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint size);
  [DllImport("kernel32.dll", SetLastError=true)] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
  [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
  [StructLayout(LayoutKind.Sequential)] struct BasicLimits { public long PerProcess, PerJob; public uint Flags; public UIntPtr MinWorkingSet, MaxWorkingSet; public uint ActiveProcesses; public UIntPtr Affinity; public uint Priority, Scheduling; }
  [StructLayout(LayoutKind.Sequential)] struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
  [StructLayout(LayoutKind.Sequential)] struct ExtendedLimits { public BasicLimits Basic; public IoCounters Io; public UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory; }
  public static IntPtr Guard(Process process) {
   IntPtr job = CreateJobObject(IntPtr.Zero, null);
   if (job == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
   var limits = new ExtendedLimits(); limits.Basic.Flags = 0x2000; // KILL_ON_JOB_CLOSE: no orphan receiver after app crash.
   int size = Marshal.SizeOf(limits); IntPtr info = Marshal.AllocHGlobal(size);
   try {
    Marshal.StructureToPtr(limits, info, false);
    if (!SetInformationJobObject(job, 9, info, (uint)size) || !AssignProcessToJobObject(job, process.Handle)) throw new System.ComponentModel.Win32Exception();
    return job;
   } catch { CloseHandle(job); throw; } finally { Marshal.FreeHGlobal(info); }
  }
 }

 public sealed class Receiver : IDisposable {
  public readonly string Root, Data;
  public Process Process { get; private set; }
  IntPtr job;
  BonjourPresence discovery;
  DateTime nextStart=DateTime.MinValue;
  public bool Discoverable { get {return discovery!=null && discovery.Present;} }
  readonly object logLock = new object();
  public event Action<string> Output;
  public DateTime Started { get; private set; }
  public int Port { get; private set; }
  public string LogFile { get { return Path.Combine(Data, "receiver.log"); } }
  public string Executable { get { return Path.Combine(Root, "engine", "bin", "uxplay.exe"); } }
  public bool Running { get { return Process != null && !Process.HasExited; } }
  public Receiver(string root, string data) { Root = root; Data = data; Directory.CreateDirectory(data); }
  public static string Quote(string value) {
   var result = new StringBuilder("\""); int slashes = 0;
   foreach (char c in value) {
    if (c == '\\') { slashes++; continue; }
    if (c == '"') { result.Append('\\', slashes * 2 + 1); result.Append(c); }
    else { result.Append('\\', slashes); result.Append(c); }
    slashes = 0;
   }
   result.Append('\\', slashes * 2); return result.Append('"').ToString();
  }
  public static List<string> Arguments(Settings s, int port, string rc, string key) {
   s.Validate();
   // Remove the sink's additional live-pipeline processing allowance in both modes.
   // Supply fullscreen options explicitly, since UxPlay only adds them to an unconfigured sink.
   string sink = "d3d11videosink processing-deadline=0 fullscreen-toggle-mode=alt-enter+property";
   if (s.OptimizeFor == "Picture") sink += " max-lateness=-1";
   if (s.Fullscreen) sink += " fullscreen=true";
   var args = new List<string> { "-n", s.Name, "-nh", "-s", s.Resolution + "@" + s.Fps, "-fps", s.Fps.ToString(), "-vs", sink, "-as", s.Audio ? "wasapisink low-latency=true" : "0", "-p", port.ToString(), "-rc", rc, "-key", key, "-nofreeze" };
   if (s.OptimizeFor == "Latency") {
    args.AddRange(new string[] { "-vsync", "no" });
    // The dropping queue is AFTER the decoder. Dropping compressed H.264/HEVC
    // packets can damage reference frames and must not be used for this setting.
   } else args.Add("-vsync");
   if (Array.IndexOf(Settings.Resolutions, s.Resolution) >= 4) args.Add("-h265");
   // -h265 must precede -s because it changes the default display size.
   if (args.Contains("-h265")) { args.Remove("-h265"); args.Insert(0, "-h265"); }
   if (s.Fullscreen) args.Add("-fs");
   if (s.DecoderChoice == "Software") args.Add("-avdec");
   return args;
  }
  public static string BonjourStatus() {
   try { using (var service = new ServiceController("Bonjour Service")) return service.Status.ToString(); }
   catch { return "Missing"; }
  }
  public static string FormatMetrics(string line) {
   var values = new Dictionary<string,double>();
   foreach (Match match in Regex.Matches(line, @"([a-z-]+)=(-?\d+(?:\.\d+)?)", RegexOptions.CultureInvariant)) {
    double value;
    if(Double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) values[match.Groups[1].Value]=value;
   }
   double fps, delay, pacing;
   if (!values.TryGetValue("rendered-fps", out fps)) return "Receiving video. Receiver timing unavailable.";
   bool known = values.TryGetValue("present-ms", out delay) && delay >= 0;
   if (!known) known = values.TryGetValue("processing-ms", out delay) && delay >= 0;
   string message = "Video: " + fps.ToString("0.#", CultureInfo.CurrentCulture) + " fps";
   if (known) message += " · Receiver: " + delay.ToString("0", CultureInfo.CurrentCulture) + " ms";
   if (values.TryGetValue("pacing-ms", out pacing) && pacing > 0) message += " · Smoothing: " + pacing.ToString("0", CultureInfo.CurrentCulture) + " ms";
   return message + " (excludes iPhone/network/display)";
  }
  static int FreePort(int preferred) {
   var global = IPGlobalProperties.GetIPGlobalProperties();
   var used = new HashSet<int>(global.GetActiveTcpListeners().Select(x => x.Port).Concat(global.GetActiveUdpListeners().Select(x => x.Port)));
   if(preferred>=7100 && !used.Contains(preferred) && !used.Contains(preferred+1) && !used.Contains(preferred+2))return preferred;
   for (int port = 7100; port < 7200; port += 3) if (!used.Contains(port) && !used.Contains(port+1) && !used.Contains(port+2)) return port;
   throw new IOException("Ports 7100–7201 are in use. Close another mirroring app and retry.");
  }
  void OnOutput(object sender, DataReceivedEventArgs e) {
   if (e.Data == null) return;
   lock (logLock) { try { File.AppendAllText(LogFile, DateTime.Now.ToString("HH:mm:ss ") + e.Data + Environment.NewLine); } catch {} }
   var callback = Output; if (callback != null) callback(e.Data);
  }
  public void Start(Settings settings) {
   if (Running) return;
   Stop(); settings.Validate();
   int settle=(int)(nextStart-DateTime.UtcNow).TotalMilliseconds;if(settle>0)Thread.Sleep(settle);
   if (!File.Exists(Executable)) throw new FileNotFoundException("The receiver engine is missing. Extract the entire AirGlass folder from the ZIP.", Executable);
   if (BonjourStatus() != "Running") throw new InvalidOperationException("Bonjour is required for iPhone discovery. Install Apple's Bonjour Print Services, or start Bonjour Service, then retry. Connection help has the official link.");
   Port = FreePort(Port);
   var rc = Path.Combine(Data, "receiver.rc"); File.WriteAllText(rc, "# AirGlass uses explicit settings.\n");
   var args = Arguments(settings, Port, rc, Path.Combine(Data, "receiver.pem"));
   var start = new ProcessStartInfo(Executable, String.Join(" ", args.Select(Quote))) { WorkingDirectory = Path.GetDirectoryName(Executable), UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
   var plugins = Path.Combine(Root, "engine", "lib", "gstreamer-1.0");
   if(settings.DecoderChoice=="Nvidia" || settings.DecoderChoice=="Auto") plugins += ";"+Path.Combine(Root,"engine","optional","nvidia");
   start.EnvironmentVariables["GST_PLUGIN_PATH"] = plugins;
   start.EnvironmentVariables["GST_PLUGIN_SYSTEM_PATH"] = plugins;
   start.EnvironmentVariables["GST_REGISTRY"] = Path.Combine(Data, "gstreamer-registry.bin");
   start.EnvironmentVariables["GST_REGISTRY_FORK"] = "no";
   start.EnvironmentVariables["AIRGLASS_LOW_LATENCY"] = settings.OptimizeFor == "Latency" ? "1" : "0";
   start.EnvironmentVariables["AIRGLASS_GPU_RENDERER"] = settings.DecoderChoice == "Software" ? "0" : "1";
   start.EnvironmentVariables["AIRGLASS_DECODER"] = settings.DecoderChoice;
   start.EnvironmentVariables["AIRGLASS_FRAME_PACING"] = settings.OptimizeFor == "Latency" && settings.SmoothLatency ? "1" : "0";
   start.EnvironmentVariables["AIRGLASS_FPS"] = settings.Fps.ToString();
   start.EnvironmentVariables["PATH"] = Path.GetDirectoryName(Executable) + ";" + Environment.GetEnvironmentVariable("PATH");
   start.EnvironmentVariables.Remove("UXPLAYRC");
   lock (logLock) File.WriteAllText(LogFile, "AirGlass receiver session " + DateTime.Now + Environment.NewLine + start.Arguments + Environment.NewLine);
   var child = new Process { StartInfo = start };
   discovery=new BonjourPresence(settings.Name);
   child.OutputDataReceived += OnOutput; child.ErrorDataReceived += OnOutput;
   try { child.Start(); job = Native.Guard(child); Process = child; Started = DateTime.UtcNow; child.BeginOutputReadLine(); child.BeginErrorReadLine(); }
   catch { try { if (!child.HasExited) child.Kill(); } catch {} child.Dispose(); throw; }
  }
  public void Stop() {
   var child = Process; Process = null;
   if (child != null) {
    try {
     if (!child.HasExited) {
      // Keep the event alive even if Stop races the child's early startup.
      using(var stop=new EventWaitHandle(false,EventResetMode.ManualReset,"Local\\AirGlass.Stop."+child.Id)) {
       stop.Set();
       if (!child.WaitForExit(5000)) child.Kill();
      }
     }
     if (child.WaitForExit(3000)) child.WaitForExit(); // Drain asynchronous stdout/stderr before disposing.
    } catch {}
    child.Dispose();
   }
   if (job != IntPtr.Zero) { Native.CloseHandle(job); job = IntPtr.Zero; }
   if(discovery!=null) {
    bool absent=discovery.WaitAbsent(2500);
    lock(logLock) {try {File.AppendAllText(LogFile,absent ? "AirGlass discovery: local removal observed; settling before replacement\n" : "AirGlass discovery: removal wait expired; settling before replacement\n");}catch{}}
    discovery.Dispose();discovery=null;nextStart=DateTime.UtcNow.AddMilliseconds(1000);
   }
  }
  public void Dispose() { Stop(); }
 }

 internal static class DiscoveryStartup {
  // Use the existing startup timeout for recovery too. Cold plugin initialization
  // must not be interrupted by a shorter, independent discovery timer.
  public const int TimeoutSeconds = 45;
  public static bool ShouldRetry(double elapsedSeconds, bool ready, bool registered, bool present, bool listening, int recoveries) {
   return !ready && recoveries < 1 && elapsedSeconds >= TimeoutSeconds && !(registered && present && listening);
  }
 }

 public sealed class Controller {
  public Window Window;
  readonly Receiver receiver;
  readonly string settingsFile;
  Settings settings;
  readonly DispatcherTimer timer;
  string failure = "";
  bool ready, closing, advertised;
  int discoveryRecoveries;
  internal Receiver TestReceiver { get { return receiver; } }
  public Controller(string root, string data) {
   receiver = new Receiver(root, data); settingsFile = Path.Combine(data, "settings.xml");
   using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("MainWindow.xaml")) Window = (Window)XamlReader.Load(stream);
   using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AirGlassLogo.png")) {
    var logo = new BitmapImage(); logo.BeginInit(); logo.CacheOption=BitmapCacheOption.OnLoad; logo.StreamSource=stream; logo.EndInit(); logo.Freeze();
    Window.Icon=logo; Get<Image>("LogoImage").Source=logo;
   }
   try { settings = Settings.Load(settingsFile); } catch (Exception ex) { settings = new Settings(); failure = "Saved settings could not be read. Defaults loaded. " + ex.Message; }
   SetControls(settings); UpdateProfile(); RefreshNetwork();
   Get<Button>("StartButton").Click += delegate { if (receiver.Running) Stop(); else Start(); };
   Get<Button>("ApplyButton").Click += delegate { Apply(); };
   Get<Button>("DiagnosticsButton").Click += delegate { ShowHelp(); };
   Get<Button>("AdvancedButton").Click += delegate {
    var panel=Get<StackPanel>("AdvancedSettings");
    bool show=panel.Visibility!=Visibility.Visible;
    panel.Visibility=show ? Visibility.Visible : Visibility.Collapsed;
    Get<Button>("AdvancedButton").Content=show ? "Settings  ⌄" : "Settings  ›";
   };
   Get<ComboBox>("PreferenceInput").SelectionChanged += delegate { UpdatePreferenceHint(); };
   Get<ComboBox>("FpsInput").SelectionChanged += delegate { UpdatePreferenceHint(); };
   Get<CheckBox>("SmoothInput").Checked += delegate { UpdatePreferenceHint(); };
   Get<CheckBox>("SmoothInput").Unchecked += delegate { UpdatePreferenceHint(); };
   receiver.Output += line => { var session=receiver.Process; if (!closing) Window.Dispatcher.BeginInvoke(new Action(() => {
    if (!Object.ReferenceEquals(session,receiver.Process)) return;
    if (line.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 || line.IndexOf("failed", StringComparison.OrdinalIgnoreCase) >= 0) failure = line;
    if (!receiver.Running) return;
    if (line.StartsWith("AirGlass discovery: registered") && line.Contains("_airplay._tcp")) advertised=true;
    if (line.StartsWith("AirGlass decoder:")) Get<TextBlock>("MessageLabel").Text=line;
    if (line.StartsWith("AirGlass metrics:")) Get<TextBlock>("MessageLabel").Text=Receiver.FormatMetrics(line);
    if (line.Contains("Begin streaming to GStreamer video pipeline")) { ready = true; State("Mirroring active", "Your screen is connected.", "Your iPhone display is in the video window. Alt + Enter toggles full screen.", "Receiving iPhone video.", true); }
    if (line.Contains("End-Of-Stream") || line.Contains("lost connection with client")) { ready = false; State("Receiver running", "Ready for your iPhone.", "Choose the receiver below in Control Center → Screen Mirroring.", "Waiting for a new connection.", true); }
   })); };
   Window.Closed += delegate { closing = true; timer.Stop(); receiver.Dispose(); };
   timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; timer.Tick += delegate { Tick(); }; timer.Start();
   bool autoStarted = false;
   Window.Loaded += delegate { if (!autoStarted) { autoStarted = true; Start(); } };
   if (failure.Length > 0) Get<TextBlock>("MessageLabel").Text = failure;
  }
  T Get<T>(string name) where T : class { return (T)Window.FindName(name); }
  void SetControls(Settings s) {
   Get<TextBox>("NameInput").Text = s.Name;
   Get<ComboBox>("ResolutionInput").SelectedIndex = Array.IndexOf(Settings.Resolutions, s.Resolution);
   Get<ComboBox>("FpsInput").SelectedIndex = Array.IndexOf(new int[] {15,30,60}, s.Fps);
   Get<CheckBox>("AudioInput").IsChecked = s.Audio; Get<CheckBox>("FullscreenInput").IsChecked = s.Fullscreen;
   Get<ComboBox>("DecoderInput").SelectedIndex = Array.IndexOf(new string[] {"Auto","Nvidia","Direct3D11","Software"}, s.DecoderChoice);
   Get<ComboBox>("PreferenceInput").SelectedIndex = s.OptimizeFor == "Picture" ? 1 : 0;
   Get<CheckBox>("SmoothInput").IsChecked = s.SmoothLatency;
   UpdatePreferenceHint();
  }
  Settings ReadControls() {
   string decoder = (string)((ComboBoxItem)Get<ComboBox>("DecoderInput").SelectedItem).Tag;
   return new Settings { Name = Get<TextBox>("NameInput").Text.Trim(), Resolution = (string)((ComboBoxItem)Get<ComboBox>("ResolutionInput").SelectedItem).Tag, Fps = Int32.Parse((string)((ComboBoxItem)Get<ComboBox>("FpsInput").SelectedItem).Tag), Audio = Get<CheckBox>("AudioInput").IsChecked == true, Fullscreen = Get<CheckBox>("FullscreenInput").IsChecked == true, Software = decoder == "Software", DecoderMode = decoder, OptimizeFor = (string)((ComboBoxItem)Get<ComboBox>("PreferenceInput").SelectedItem).Tag, SmoothLatency = Get<CheckBox>("SmoothInput").IsChecked == true };
  }
  void UpdatePreferenceHint() {
   bool picture = Get<ComboBox>("PreferenceInput").SelectedIndex == 1;
   Get<CheckBox>("SmoothInput").IsEnabled = !picture;
   int fps = new int[] {15,30,60}[Math.Max(0, Get<ComboBox>("FpsInput").SelectedIndex)];
     Get<CheckBox>("SmoothInput").ToolTip = "Adapts to recent timing variation, starting near 8 ms and capped at " + Math.Round(1000.0/fps) + " ms. Sheds temporary waiting at 4 ms per second. Turn off for immediate display.";
   Get<TextBlock>("PreferenceHint").Text = Get<ComboBox>("PreferenceInput").SelectedIndex == 1
    ? "Preserve frames and audio/video timing. May add delay."
    : Get<CheckBox>("SmoothInput").IsChecked == true
     ? "Adapt smoothing to recent jitter; use less waiting on steady streams. Audio timing may drift."
     : "Show frames immediately for the shortest delay. Uneven arrivals may look jittery.";
  }
  void UpdateProfile() {
   Get<TextBlock>("ReceiverLabel").Text = settings.Name;
   Get<TextBlock>("ConnectStep").Text = "3   Select " + settings.Name + " from the list.";
   Get<TextBlock>("ProfileSummary").Text = settings.Resolution.Split('x')[1] + "p / " + settings.Fps + " fps limit";
   Get<TextBlock>("SettingsFeedback").Text = settings.OptimizeFor == "Picture" ? "Best picture · starts automatically" : "Best latency · starts automatically";
  }
  void State(string badge, string title, string hint, string message, bool live) {
   Get<TextBlock>("StatusBadge").Text = badge; Get<TextBlock>("HeroTitle").Text = title;
   Get<TextBlock>("HeroHint").Text = hint; Get<TextBlock>("MessageLabel").Text = message;
   Get<System.Windows.Shapes.Ellipse>("StatusDot").Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(live ? "#70E4CA" : "#94A3B8"));
   Get<Button>("StartButton").Content = receiver.Running ? "Stop receiver" : "Start receiver";
   Get<Button>("ApplyButton").Content = receiver.Running ? "Save & restart receiver" : "Save settings";
  }
  public void Start(bool recovery=false) {
   if(!recovery)discoveryRecoveries=0;
   try {
    settings = ReadControls(); settings.Validate(); settings.Save(settingsFile); UpdateProfile();
    failure = ""; ready = false; advertised = false; receiver.Start(settings);
    State("Starting…", "Preparing your receiver…", "Loading audio and video. First launch may take a little longer.", "Preparing AirPlay discovery…", false);
   } catch (Exception ex) { State("Needs attention", "Let's get connected.", ex.Message, "Open Connection help for setup and diagnostics.", false); }
  }
  public void Stop() { receiver.Stop(); ready = false; State("Receiver off", "Make room for your screen.", "Start the receiver, then connect from your iPhone.", "Receiver stopped.", false); }
  public void Apply() {
   try {
    var updated = ReadControls(); updated.Validate(); updated.Save(settingsFile);
    bool unchanged= settings.Name==updated.Name && settings.Resolution==updated.Resolution && settings.Fps==updated.Fps && settings.Audio==updated.Audio && settings.Fullscreen==updated.Fullscreen && settings.DecoderChoice==updated.DecoderChoice && settings.OptimizeFor==updated.OptimizeFor && settings.SmoothLatency==updated.SmoothLatency;
    bool restart = receiver.Running && !unchanged; settings = updated; UpdateProfile();
    if (restart) { Stop(); Start(); }
    Get<TextBlock>("SettingsFeedback").Text = restart ? "Saved. Reconnect Screen Mirroring on your iPhone." : "Settings saved for your next connection.";
   } catch (Exception ex) { Get<TextBlock>("SettingsFeedback").Text = ex.Message; }
  }
  void Tick() {
   if (receiver.Process == null) return;
   if (!receiver.Running) {
    int exit = receiver.Process.ExitCode; receiver.Stop();
    State("Needs attention", "Receiver stopped unexpectedly.", failure.Length > 0 ? failure : "Exit code " + exit + ". Connection help includes the receiver log.", "Try another video decoder, then start again.", false); return;
   }
   if (ready) return; // Startup recovery must never interrupt a connected session.
   bool present=receiver.Discoverable;
   bool listening=Native.HasListener(receiver.Process.Id);
   if (advertised && present && listening) {
    ready = true; State("Receiver running", "Ready for your iPhone.", "Choose the receiver below in Control Center → Screen Mirroring.", "AirPlay server listening. Waiting for your iPhone.", true);
   } else if (DiscoveryStartup.ShouldRetry((DateTime.UtcNow-receiver.Started).TotalSeconds,ready,advertised,present,listening,discoveryRecoveries)) {
    discoveryRecoveries++;Stop();Start(true);
    // Preserve the error from Start if replacement fails.
    if(receiver.Running)Get<TextBlock>("SettingsFeedback").Text="Retrying AirPlay discovery after an unconfirmed startup…";
   } else if ((DateTime.UtcNow - receiver.Started).TotalSeconds >= DiscoveryStartup.TimeoutSeconds) {
    Get<TextBlock>("MessageLabel").Text = "AirPlay startup could not be confirmed. Open Connection help to check Bonjour and the receiver log.";
   }
  }
  public static string NetworkSummary() {
   var addresses = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback).SelectMany(n => n.GetIPProperties().UnicastAddresses).Where(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).Select(a => a.Address.ToString()).ToArray();
   return addresses.Length == 0 ? "No active IPv4 network" : "PC address: " + String.Join(" · ", addresses);
  }
  void RefreshNetwork() { try { Get<TextBlock>("NetworkLabel").Text = NetworkSummary(); } catch { Get<TextBlock>("NetworkLabel").Text = "Network details unavailable"; } }
  void ShowHelp() {
   RefreshNetwork();
   var help = new Window { Owner = Window, Title = "AirGlass • Connection help", Width = 680, Height = 640, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = new SolidColorBrush(Color.FromRgb(20,30,44)), Foreground = Brushes.White, FontFamily = new FontFamily("Segoe UI") };
   var panel = new StackPanel { Margin = new Thickness(25) }; help.Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
   panel.Children.Add(new TextBlock { Text = "Connect your iPhone", FontSize = 23, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0,0,0,18) });
   panel.Children.Add(new TextBlock { Text = "1. Connect both devices to the same local Wi-Fi network.\n2. Open AirGlass — the receiver starts automatically.\n3. On iPhone, open Control Center → Screen Mirroring → " + settings.Name + ".\n4. Allow the receiver through Windows Firewall on your private/home network if Windows asks.\n\nBest latency skips old decoded frames. Adaptive frame pacing uses a small cushion that follows recent jitter; turn it off for the shortest delay. Audio/video timing may drift. Best picture preserves frames and audio/video timing. Both use lower-latency audio output. Try 720p at 30 fps if your PC or Wi-Fi struggles. Automatic checks available codecs and prefers Direct3D11 GPU decoding, then NVIDIA NVDEC, then software. Direct3D11 uses NVIDIA hardware on compatible NVIDIA GPUs. Manual decoder choices remain available. Compare modes because decoder buffering and CPU load vary. Connecting the PC to the router with Ethernet can reduce wireless contention. Save and reconnect after changing settings.\n\nIf AirGlass does not appear, check that the PC network is Private, turn off a VPN temporarily, and avoid guest Wi-Fi with device isolation.\n\nProtected streaming video may appear black. Resolution is a request and FPS is a maximum; neither is a guaranteed measured rate.", TextWrapping = TextWrapping.Wrap, FontSize = 14, LineHeight = 23 });
   panel.Children.Add(new TextBlock { Text = "\nDiagnostics\nBonjour: " + Receiver.BonjourStatus() + "\n" + NetworkSummary() + "\nEngine: " + (File.Exists(receiver.Executable) ? "Present · AirGlass native 2.7 / UxPlay 1.72" : "Missing") + "\nReceiver: " + (receiver.Running ? "Running · PID " + receiver.Process.Id + " · ports " + receiver.Port + "–" + (receiver.Port+2) : "Stopped") + "\nLog: " + receiver.LogFile, TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 21, Margin = new Thickness(0,16,0,14) });
   AddHelpButton(panel, "Open receiver log", () => { if (File.Exists(receiver.LogFile)) Process.Start("notepad.exe", Receiver.Quote(receiver.LogFile)); else MessageBox.Show(help, "Start the receiver once to create a log."); });
   AddHelpButton(panel, "Open Windows Firewall", () => Process.Start(new ProcessStartInfo("control.exe", "/name Microsoft.WindowsFirewall") { UseShellExecute = true }));
   AddHelpButton(panel, "Get Apple Bonjour Print Services", () => Process.Start(new ProcessStartInfo("https://support.apple.com/kb/DL999") { UseShellExecute = true }));
   help.ShowDialog();
  }
  void AddHelpButton(StackPanel panel, string label, Action action) {
   var button = new Button { Content = label, Padding = new Thickness(12,8,12,8), Margin = new Thickness(0,4,0,4) };
   button.Click += delegate { try { action(); } catch (Exception ex) { MessageBox.Show(Window, ex.Message, "AirGlass"); } }; panel.Children.Add(button);
  }
  public void Render(string output) {
   Window.Width = 1100; Window.Height = 870; Window.Measure(new Size(1100,870)); Window.Arrange(new Rect(0,0,1100,870)); Window.UpdateLayout();
   var target = new RenderTargetBitmap(1100,870,96,96,PixelFormats.Pbgra32); target.Render(Window);
   var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(target)); using (var file = File.Create(output)) png.Save(file);
  }
 }

 public static class Program {
  [STAThread] public static int Main(string[] args) {
   string root = AppDomain.CurrentDomain.BaseDirectory;
   try {
    if (args.Length > 0 && args[0] == "--self-test") return SelfTest(root, args.Length > 1 ? args[1] : Path.Combine(root, "test-results"));
    if (args.Length==2 && args[0]=="--startup-test") return StartupTest(args[1]);
    if (args.Length==2 && args[0]=="--restart-test") return RestartTest(root,args[1]);
    bool created;
    using (var mutex = new Mutex(true, "Local\\AirGlass.Receiver", out created)) {
     if (!created) { MessageBox.Show("AirGlass is already open. Use the existing window.", "AirGlass"); return 0; }
     try {
      var app = new Application();
      string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AirGlass");
      if (args.Length == 2 && args[0] == "--decoder") {
       string file=Path.Combine(data,"settings.xml");
       var launchSettings=Settings.Load(file);
       launchSettings.DecoderMode=args[1]; launchSettings.Software=args[1]=="Software";
       launchSettings.Save(file);
      }
      app.DispatcherUnhandledException += (s,e) => { MessageBox.Show(e.Exception.Message, "AirGlass"); e.Handled = true; };
      var controller = new Controller(root, data); return app.Run(controller.Window);
     } finally { mutex.ReleaseMutex(); }
    }
   } catch (Exception ex) { MessageBox.Show(ex.ToString(), "AirGlass could not start"); return 1; }
  }
  static int RestartTest(string root,string directory) {
   Directory.CreateDirectory(directory);var report=new StringBuilder();
   try {
    using(var receiver=new Receiver(root,Path.Combine(directory,"receiver"))) {
     int port=0;
     for(int i=0;i<8;i++) {
      var watch=Stopwatch.StartNew();
      receiver.Start(new Settings {Name="AirGlass Settings Restart QA",DecoderMode="Direct3D11",Fps=new int[]{15,30,60}[i%3],Audio=i%2==0,OptimizeFor=i%2==0 ? "Latency" : "Picture"});
      if(i>0)Assert(watch.ElapsedMilliseconds>=900,"Restart observes the Bonjour settling interval",report);
      var until=DateTime.UtcNow.AddSeconds(20);
      while(receiver.Running && !receiver.Discoverable && DateTime.UtcNow<until)Thread.Sleep(50);
      Assert(receiver.Running && receiver.Discoverable && Native.HasListener(receiver.Process.Id),"Same-name settings restart "+(i+1)+" is locally discoverable and listening",report);
      if(port==0)port=receiver.Port;
      Assert(receiver.Port==port,"Restart retains the advertised port block",report);
      receiver.Stop();string log=File.ReadAllText(receiver.LogFile);
      Assert(log.Contains("AirGlass discovery: services withdrawn") && log.Contains("AirGlass discovery: local removal observed"),"Goodbye and local removal precede replacement",report);
     }
    }
    File.WriteAllText(Path.Combine(directory,"results.txt"),report.ToString());return 0;
   }catch(Exception ex){report.AppendLine("FAIL: "+ex);File.WriteAllText(Path.Combine(directory,"results.txt"),report.ToString());return 1;}
  }
  static void TestDiscoveryStartup(StringBuilder report) {
    foreach(bool registered in new bool[]{false,true})
     foreach(bool present in new bool[]{false,true})
      foreach(bool listening in new bool[]{false,true}) {
       Assert(!DiscoveryStartup.ShouldRetry(12,false,registered,present,listening,0),"Do not interrupt cold initialization after 12 seconds",report);
       Assert(!DiscoveryStartup.ShouldRetry(44.9,false,registered,present,listening,0),"Allow the full startup window",report);
       Assert(DiscoveryStartup.ShouldRetry(45,false,registered,present,listening,0)==!(registered && present && listening),"Retry every incomplete readiness combination at the deadline",report);
       Assert(!DiscoveryStartup.ShouldRetry(90,true,registered,present,listening,0),"Never restart an established session",report);
       Assert(!DiscoveryStartup.ShouldRetry(90,false,registered,present,listening,1),"Keep automatic recovery bounded to one retry",report);
      }
  }
  static int StartupTest(string directory) {
   Directory.CreateDirectory(directory); var report=new StringBuilder();
   try { TestDiscoveryStartup(report); File.WriteAllText(Path.Combine(directory,"startup-results.txt"),report.ToString()); return 0; }
   catch(Exception ex) { File.WriteAllText(Path.Combine(directory,"startup-results.txt"),report.ToString()+ex); return 1; }
  }
  static int SelfTest(string root, string testRoot) {
   Directory.CreateDirectory(testRoot); var report = new StringBuilder();
   try {
    TestDiscoveryStartup(report);
    var settings = new Settings(); settings.Validate();
    Assert(settings.Resolution == "1920x1080" && settings.Fps == 30, "Defaults are 1080p / 30 fps", report);
    Assert(settings.OptimizeFor == "Latency" && settings.SmoothLatency, "Best latency with smooth pacing is the default", report);
    Assert(settings.DecoderChoice == "Direct3D11", "New profiles default to Direct3D11", report);
    foreach (string decoder in new string[] {"Auto","Nvidia","Direct3D11","Software"}) {
     var choice = new Settings { DecoderMode=decoder };
     choice.Validate();
     var flags = Receiver.Arguments(choice,7100,"rc","key");
     Assert(flags.Contains("-avdec") == (decoder=="Software"), "Decoder preference "+decoder+" preserves hardware/software flag mapping", report);
    }
    string timing=Receiver.FormatMetrics("AirGlass metrics: rendered-fps=29.9 decode-ms=1.6 processing-ms=2.1 present-ms=11.2 pacing-ms=8.1");
    Assert(timing.Contains("29") && timing.Contains("Receiver: 11 ms") && timing.Contains("Smoothing: 8 ms") && timing.Contains("excludes iPhone/network/display"), "UI shows actual sink FPS and measured receiver timing with scope", report);
    Assert(!Receiver.FormatMetrics("AirGlass metrics: rendered-fps=0 present-ms=-1 processing-ms=-1 pacing-ms=0").Contains("Receiver:"), "Unavailable timing is not shown as a measured delay", report);
    foreach (var res in Settings.Resolutions) foreach (var fps in new int[] {15,30,60}) {
     settings.Resolution = res; settings.Fps = fps;
     var argv = Receiver.Arguments(settings,7100,"C:\\a b\\config.rc","C:\\a b\\key.pem");
     Assert(argv[argv.IndexOf("-s")+1] == res+"@"+fps && argv[argv.IndexOf("-fps")+1] == fps.ToString(), res+" / "+fps+" correctly configures engine", report);
    }
    var fast = Receiver.Arguments(new Settings(),7100,"rc","key");
    Assert(fast[fast.IndexOf("-vsync")+1] == "no" && !fast.Contains("-vc"), "Best latency uses native queue tuning and bypasses absolute timestamp waits", report);
    Assert(fast[fast.IndexOf("-as")+1].Contains("low-latency=true") && fast[fast.IndexOf("-vs")+1].Contains("processing-deadline=0"), "Low-latency audio and video processing are configured", report);
    var picture = Receiver.Arguments(new Settings { OptimizeFor="Picture" },7100,"rc","key");
    Assert(picture.Contains("-vsync") && !picture.Contains("no") && !picture.Contains("-vc") && picture[picture.IndexOf("-vs")+1].Contains("max-lateness=-1") && picture[picture.IndexOf("-as")+1].Contains("low-latency=true"), "Best picture preserves timing and late frames while using low-latency audio",report);
    settings = new Settings { Name = "My iPhone Receiver", Fps = 60, Resolution = "1280x720", Audio = false, Software = true, Fullscreen = true, OptimizeFor="Picture", SmoothLatency=false };
    string file = Path.Combine(testRoot,"settings.xml"); settings.Save(file); var loaded = Settings.Load(file);
    Assert(loaded.Name == settings.Name && loaded.Fps == 60 && !loaded.Audio && loaded.Software && loaded.Fullscreen && loaded.OptimizeFor == "Picture" && !loaded.SmoothLatency, "All settings including pacing preference persist across restart", report);
    Assert(loaded.DecoderChoice == "Software", "Legacy software flag still selects the software decoder", report);
    string nvidiaFile=Path.Combine(testRoot,"nvidia.xml");
    new Settings { DecoderMode="Nvidia", Fps=60 }.Save(nvidiaFile);
    Assert(Settings.Load(nvidiaFile).DecoderChoice=="Nvidia", "NVIDIA decoder preference persists", report);
    settings.Save(file); Assert(Settings.Load(file).Name == settings.Name, "Existing settings are replaced atomically", report);
    string legacyFile = Path.Combine(testRoot,"legacy.xml");
    File.WriteAllText(legacyFile,Regex.Replace(File.ReadAllText(file).Replace("<OptimizeFor>Picture</OptimizeFor>", "").Replace("<SmoothLatency>false</SmoothLatency>", ""), "<DecoderMode>.*?</DecoderMode>", ""));
    var migrated=Settings.Load(legacyFile);
    Assert(migrated.OptimizeFor == "Latency" && migrated.SmoothLatency && migrated.Fps == 60 && migrated.Name == settings.Name, "Old settings migrate to smooth Best latency without losing saved quality", report);
    Assert(migrated.DecoderChoice == "Software", "Pre-2.3 profiles retain their saved decoder choice", report);
    var custom = Receiver.Arguments(settings,7100,"rc","key");
    Assert(custom.Contains("-fs") && custom.Contains("-avdec") && custom[custom.IndexOf("-as")+1] == "0", "Audio, fullscreen and software decoding map to engine flags", report);
    settings.Fps = 120; bool rejected = false; try { settings.Validate(); } catch (ArgumentException) { rejected = true; }
    Assert(rejected,"Invalid FPS rejected",report);
    Assert(Receiver.Quote("a b") == "\"a b\"" && Receiver.Quote("a\"b") == "\"a\\\"b\"", "Names and paths are quoted safely", report);
    var app = new Application(); var ui = new Controller(root, Path.Combine(testRoot,"ui-data"));
    ui.Window.Show(); PumpDispatcher();
    if (Receiver.BonjourStatus() == "Running") {
     Assert(ui.TestReceiver.Running,"Opening the app automatically starts the receiver",report);
     var autoUntil=DateTime.UtcNow.AddSeconds(45);
     while (ui.TestReceiver.Running && !Native.HasListener(ui.TestReceiver.Process.Id) && DateTime.UtcNow < autoUntil) { PumpDispatcher(); Thread.Sleep(100); }
     Assert(ui.TestReceiver.Running && Native.HasListener(ui.TestReceiver.Process.Id), "Automatically started receiver listens for AirPlay",report);
     Thread.Sleep(1100); PumpDispatcher();
    }
    Assert(ui.TestReceiver.Discoverable,"Bonjour observer confirms the receiver is discoverable",report);
    int originalPid=ui.TestReceiver.Process.Id;ui.Apply();
    Assert(ui.TestReceiver.Process.Id==originalPid,"Saving unchanged settings retains the active receiver",report);
    ui.Render(Path.Combine(testRoot,"preview.png"));
    Assert(((ComboBox)ui.Window.FindName("ResolutionInput")).SelectedIndex == 3, "UI defaults to 1080p", report);
    Assert(((ComboBox)ui.Window.FindName("FpsInput")).SelectedIndex == 1, "UI defaults to 30 fps", report);
    Assert(((ComboBox)ui.Window.FindName("PreferenceInput")).SelectedIndex == 0, "UI defaults to Best latency",report);
    Assert(((ComboBox)ui.Window.FindName("DecoderInput")).SelectedIndex == 2, "UI defaults to Direct3D11 decoder",report);
    Assert(((CheckBox)ui.Window.FindName("SmoothInput")).IsChecked == true, "Smooth pacing is enabled by default",report);
    Assert(((StackPanel)ui.Window.FindName("AdvancedSettings")).Visibility==Visibility.Collapsed,"Additional settings are hidden initially",report);
    Assert(((CheckBox)ui.Window.FindName("AudioInput")).IsChecked==true,"Audio switch starts on",report);
    ((CheckBox)ui.Window.FindName("AudioInput")).IsChecked=false; ui.Apply();
    Assert(!Settings.Load(Path.Combine(testRoot,"ui-data","settings.xml")).Audio,"Audio switch saves off",report);
    ((CheckBox)ui.Window.FindName("AudioInput")).IsChecked=true; ui.Apply();
    Assert(Settings.Load(Path.Combine(testRoot,"ui-data","settings.xml")).Audio,"Audio switch saves on",report);
    ((Button)ui.Window.FindName("AdvancedButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Assert(((StackPanel)ui.Window.FindName("AdvancedSettings")).Visibility==Visibility.Visible,"Settings button reveals additional controls",report);
    ui.Render(Path.Combine(testRoot,"advanced-preview.png"));
    ((Button)ui.Window.FindName("AdvancedButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Assert(((StackPanel)ui.Window.FindName("AdvancedSettings")).Visibility==Visibility.Collapsed,"Settings button hides additional controls",report);
    ((TextBox)ui.Window.FindName("NameInput")).Text = "AirGlass Test";
    ((ComboBox)ui.Window.FindName("FpsInput")).SelectedIndex = 0;
    ((Button)ui.Window.FindName("ApplyButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Assert(Settings.Load(Path.Combine(testRoot,"ui-data","settings.xml")).Fps == 15, "Save button persists a changed FPS",report);
    ((ComboBox)ui.Window.FindName("PreferenceInput")).SelectedIndex = 1; ui.Apply();
    Assert(Settings.Load(Path.Combine(testRoot,"ui-data","settings.xml")).OptimizeFor == "Picture", "UI saves the Best picture preference", report);
    Assert(!((CheckBox)ui.Window.FindName("SmoothInput")).IsEnabled,"Pacing checkbox is disabled in Best picture mode",report);
    ((ComboBox)ui.Window.FindName("PreferenceInput")).SelectedIndex = 0;
    ((CheckBox)ui.Window.FindName("SmoothInput")).IsChecked = false; ui.Apply();
    Assert(!Settings.Load(Path.Combine(testRoot,"ui-data","settings.xml")).SmoothLatency,"UI saves immediate-display preference",report);
    ((ComboBox)ui.Window.FindName("DecoderInput")).SelectedIndex = 1; ui.Apply();
    Assert(Settings.Load(Path.Combine(testRoot,"ui-data","settings.xml")).DecoderChoice=="Nvidia", "UI saves NVIDIA decoder selection", report);
    ((TextBox)ui.Window.FindName("NameInput")).Text = " "; ui.Apply();
    Assert(Settings.Load(Path.Combine(testRoot,"ui-data","settings.xml")).Name == "AirGlass Test", "Invalid name does not overwrite saved settings", report);
    ui.Stop(); PumpDispatcher();
    Assert(!ui.TestReceiver.Running,"Manual Stop remains stopped while the app is open",report);
    ui.Window.Close();
    if (Receiver.BonjourStatus() == "Running") {
     foreach (string mode in new string[] {"Latency","Picture"}) foreach (int fps in new int[] {15,30,60}) using (var receiver = new Receiver(root, Path.Combine(testRoot,"engine-"+mode+"-"+fps))) {
      bool software=fps==15;
      string decoder = fps==15 ? "Software" : fps==60 ? "Nvidia" : "Direct3D11";
      receiver.Start(new Settings { Name="AirGlass QA "+mode+" "+fps, Fps=fps, OptimizeFor=mode, Fullscreen=true, Software=software, DecoderMode=decoder, SmoothLatency=fps!=15 });
      var until = DateTime.UtcNow.AddSeconds(45); bool listener = false;
      while (DateTime.UtcNow < until && receiver.Running) { if (Native.HasListener(receiver.Process.Id)) { listener = true; break; } Thread.Sleep(250); }
      Assert(listener, "Real receiver opens an AirPlay TCP listener at "+fps+" fps in "+mode+" mode", report);
      int pid=receiver.Process.Id; receiver.Stop();
      string engineLog=File.ReadAllText(receiver.LogFile);
      Assert(engineLog.IndexOf("error",StringComparison.OrdinalIgnoreCase)<0 && !engineLog.Contains("no property"), "Native video/audio pipeline accepts "+mode+" tuning and fullscreen options", report);
      if(software) Assert(engineLog.Contains("AirGlass decoder: avdec_h264 (software)"),"Native software compatibility decoder initializes in "+mode+" mode",report);
      if(!software) Assert(engineLog.Contains(fps==60 ? "AirGlass preferred decoder: nvh264dec" : "AirGlass preferred decoder: d3d11h264dec"),"Native hardware preference initializes in "+mode+" mode",report);
      if(mode=="Latency") Assert(engineLog.Contains(fps==15 ? "AirGlass pacing: immediate display" : "AirGlass pacing: adaptive after decode"),"Native pacing preference initializes at "+fps+" fps",report);
      bool stopped=false; try { Process.GetProcessById(pid); } catch (ArgumentException) { stopped=true; }
      Assert(stopped,"Stop terminates receiver and releases ports",report);
      Assert(engineLog.Contains("AirGlass shutdown: graceful stop requested") && engineLog.Contains("AirGlass discovery: services withdrawn"),"Stop withdraws Bonjour services gracefully",report);
     }
    } else report.AppendLine("SKIP: native receiver integration requires Bonjour Service.");
    report.AppendLine("Manual iPhone connection, actual stream resolution/FPS and audio remain unverified without an iPhone.");
    File.WriteAllText(Path.Combine(testRoot,"results.txt"),report.ToString()); return 0;
   } catch (Exception ex) { report.AppendLine("FAIL: "+ex); File.WriteAllText(Path.Combine(testRoot,"results.txt"),report.ToString()); return 1; }
  }
  static void PumpDispatcher() {
   var frame=new DispatcherFrame();
   Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(() => frame.Continue=false));
   Dispatcher.PushFrame(frame);
  }
  static void Assert(bool condition, string label, StringBuilder report) { if (!condition) throw new Exception(label); report.AppendLine("PASS: "+label); }
 }
}
