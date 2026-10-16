// AirGlass local Bonjour lifecycle observer. GPL-3.0-or-later.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace AirGlass {
 internal sealed class BonjourPresence : IDisposable {
  [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void Reply(IntPtr reference,uint flags,uint index,int error,IntPtr name,IntPtr type,IntPtr domain,IntPtr context);
  [DllImport("dnssd.dll",CallingConvention=CallingConvention.StdCall)] static extern int DNSServiceBrowse(out IntPtr reference,uint flags,uint index,string type,string domain,Reply callback,IntPtr context);
  [DllImport("dnssd.dll",CallingConvention=CallingConvention.StdCall)] static extern int DNSServiceRefSockFD(IntPtr reference);
  [DllImport("dnssd.dll",CallingConvention=CallingConvention.StdCall)] static extern int DNSServiceProcessResult(IntPtr reference);
  [DllImport("dnssd.dll",CallingConvention=CallingConvention.StdCall)] static extern void DNSServiceRefDeallocate(IntPtr reference);
  [StructLayout(LayoutKind.Sequential)] struct Timeout { public int Seconds,Microseconds; }
  [DllImport("ws2_32.dll",CallingConvention=CallingConvention.StdCall)] static extern int select(int unused,IntPtr read,IntPtr write,IntPtr except,ref Timeout timeout);
  readonly object gate=new object(); readonly HashSet<uint> interfaces=new HashSet<uint>();
  readonly Thread worker; readonly string name; volatile bool stopping; bool observed;
  public bool Present { get { lock(gate) return interfaces.Count>0; } }
  public BonjourPresence(string receiverName) { name=receiverName; worker=new Thread(Run) {IsBackground=true,Name="AirGlass Bonjour observer"};worker.Start(); }
  static string Utf8(IntPtr value) { int n=0;while(Marshal.ReadByte(value,n)!=0)n++;var bytes=new byte[n];Marshal.Copy(value,bytes,0,n);return Encoding.UTF8.GetString(bytes); }
  public bool WaitAbsent(int milliseconds) {
   var until=DateTime.UtcNow.AddMilliseconds(milliseconds);
   lock(gate) {
    while(observed && interfaces.Count>0 && DateTime.UtcNow<until) Monitor.Wait(gate,Math.Max(1,(int)(until-DateTime.UtcNow).TotalMilliseconds));
    return interfaces.Count==0;
   }
  }
  void Run() {
   IntPtr reference=IntPtr.Zero,fdset=Marshal.AllocHGlobal(8+64*IntPtr.Size);
   Reply callback=(r,flags,index,error,service,type,domain,context)=> {
    if(error!=0 || Utf8(service)!=name)return;
    lock(gate) {observed=true;if((flags&2)!=0)interfaces.Add(index);else interfaces.Remove(index);Monitor.PulseAll(gate);}
   };
   try {
    if(DNSServiceBrowse(out reference,0,0,"_airplay._tcp",null,callback,IntPtr.Zero)!=0)return;
    int fd=DNSServiceRefSockFD(reference);if(fd<0)return;
    while(!stopping) {
     Marshal.WriteInt32(fdset,1);Marshal.WriteIntPtr(fdset,8,new IntPtr(fd));
     var timeout=new Timeout {Microseconds=100000};int result=select(0,fdset,IntPtr.Zero,IntPtr.Zero,ref timeout);
     if(result<0)break;
     if(result>0 && DNSServiceProcessResult(reference)!=0)break;
    }
   } catch(DllNotFoundException) {} catch(EntryPointNotFoundException) {}
   finally { if(reference!=IntPtr.Zero)DNSServiceRefDeallocate(reference);Marshal.FreeHGlobal(fdset);GC.KeepAlive(callback); }
  }
  public void Dispose() {stopping=true;worker.Join(2000);}
 }
}
