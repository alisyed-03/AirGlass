using System;
using System.IO;
using AirGlass;
class AdaptiveQualityTest {
 static void Check(bool condition,string label) {if(!condition)throw new Exception(label);}
 static string Metric(double delay,double jitter=0,double fps=30) {return "AirGlass metrics: rendered-fps="+fps.ToString(System.Globalization.CultureInfo.InvariantCulture)+" present-ms="+delay.ToString(System.Globalization.CultureInfo.InvariantCulture)+" jitter-ms="+jitter.ToString(System.Globalization.CultureInfo.InvariantCulture);}
 static int Main() {
  var policy=new AdaptiveQuality(0);
  for(int i=0;i<28;i++)Check(!policy.Observe(Metric(200),i),"Startup/sustained window");
  Check(policy.Observe(Metric(200),28),"Sustained overload lowers quality");
  for(int i=29;i<96;i++)Check(!policy.Observe(Metric(200),i),"Cooldown and sustained window");
  Check(policy.Observe(Metric(200),96),"Overload after cooldown");
  policy=new AdaptiveQuality(0);
  for(int i=20;i<100;i++)Check(!policy.Observe(Metric(i%2==0?200:20),i),"Spikes recover");
  for(int i=100;i<130;i++)Check(!policy.Observe(Metric(200,0,0),i),"Static screens don't adapt");
  policy=new AdaptiveQuality(0);
  for(int i=20;i<28;i++)Check(!policy.Observe(Metric(20,50),i),"Jitter window");
  Check(policy.Observe(Metric(20,50),28),"Sustained jitter adapts");
  policy=new AdaptiveQuality(0);
  for(int i=20;i<28;i++)policy.Observe(Metric(200),i);
  Check(!policy.Observe(Metric(200),40),"Telemetry gap clears evidence");
  Check(!policy.Observe("AirGlass metrics: rendered-fps=30 present-ms=-1 jitter-ms=0",41),"Unavailable delay");
  Check(!policy.Observe("AirGlass metrics: rendered-fps=30 present-ms=200",42),"Missing jitter");
  Check(!policy.Observe("unrelated output",43),"Unrelated telemetry");
  policy=new AdaptiveQuality(0);
  for(int i=20;i<28;i+=2)Check(!policy.Observe(Metric(200),i),"Native two-second metric cadence");
  Check(policy.Observe(Metric(200),28),"Native cadence triggers after eight seconds");
  int resolution=3,fps=60;
  Check(AdaptiveQuality.Lower(ref resolution,ref fps)&&resolution==3&&fps==30,"FPS first");
  Check(AdaptiveQuality.Lower(ref resolution,ref fps)&&resolution==2&&fps==30,"Resolution step");
  Check(AdaptiveQuality.Lower(ref resolution,ref fps)&&resolution==1,"720p floor");
  Check(AdaptiveQuality.Lower(ref resolution,ref fps)&&fps==15,"Minimum FPS");
  Check(!AdaptiveQuality.Lower(ref resolution,ref fps),"Floor stops restarts");
  resolution=0;fps=30;AdaptiveQuality.Lower(ref resolution,ref fps);
  Check(resolution==0&&fps==15,"Never raise a lower maximum");
  var settings=new Settings {Adaptive=true};var active=settings.Copy();active.Resolution="1280x720";active.Fps=15;
  Check(settings.Resolution=="1920x1080"&&settings.Fps==30,"Session changes preserve maximum");
  string folder=Path.Combine(Path.GetTempPath(),"AirGlass-adaptive-"+Guid.NewGuid());Directory.CreateDirectory(folder);
  try {
   string file=Path.Combine(folder,"settings.xml");settings.Save(file);
   Check(Settings.Load(file).Adaptive,"Opt-in persists");
   File.WriteAllText(file,File.ReadAllText(file).Replace("<Adaptive>true</Adaptive>",""));
   Check(!Settings.Load(file).Adaptive,"Older profiles default off");
  }finally {Directory.Delete(folder,true);}
  Console.WriteLine("PASS: adaptive windows, cooldown, telemetry guards, quality bounds and persistence");return 0;
 }
}
