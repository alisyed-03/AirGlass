// GPL-3.0-or-later. Receiver-side overload/jitter feedback, not an internet speed test.
using System;
using System.Globalization;
using System.Text.RegularExpressions;
namespace AirGlass {
 public sealed class AdaptiveQuality {
  double started, lastSample=Double.NaN, badSince=Double.NaN;
  public AdaptiveQuality(double now) { started=now; }
  static bool Number(string line,string key,out double value) {
   var match=Regex.Match(line,@"(?:^|\s)"+key+@"=(-?\d+(?:\.\d+)?)(?:\s|$)");
   return Double.TryParse(match.Groups[1].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out value) && !Double.IsNaN(value) && !Double.IsInfinity(value);
  }
  public bool Observe(string line,double now) {
   double fps,present,jitter;
   if(!line.StartsWith("AirGlass metrics:") || !Number(line,"rendered-fps",out fps) || !Number(line,"present-ms",out present) || !Number(line,"jitter-ms",out jitter) || fps<=1 || present<0 || jitter<0) {badSince=Double.NaN;lastSample=Double.NaN;return false;}
   // No adaptation during startup, after stale telemetry, or on a single spike.
   if(Double.IsNaN(lastSample) || now-lastSample>3 || now<=lastSample)badSince=Double.NaN;
   lastSample=now;
   if(now-started<20 || (present<=150 && jitter<=40)) {badSince=Double.NaN;return false;}
   if(Double.IsNaN(badSince))badSince=now;
   if(now-badSince<8)return false;
   badSince=Double.NaN;started=now+40; // 60 seconds before collecting another bad interval.
   return true;
  }
  public static bool Lower(ref int resolutionIndex,ref int fps) {
   if(fps>30) {fps=30;return true;}
   if(resolutionIndex>1) {resolutionIndex--;return true;}
   if(fps>15) {fps=15;return true;}
   return false;
  }
 }
}
