using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;
namespace AdamDrop {
class ShareTest {
 [STAThread] static int Main() {
  Config.Port=8765; Config.Key=Guid.NewGuid().ToString("N"); Config.SaveDir=Path.Combine(Config.BaseDir,"received"); Config.KeepAwake=false; Config.OpenFolder=false; Directory.CreateDirectory(Config.SaveDir);
  var server=new Server();
  var add=typeof(Server).GetMethod("AddSharedFiles",BindingFlags.Instance|BindingFlags.NonPublic);
  if(add==null) { Console.WriteLine("FAIL: selected-file sharing is missing"); return 1; }
  string fixture=Path.Combine(Config.BaseDir,"ảnh thử + test.tệp"); byte[] bytes=new byte[1048579]; for(int i=0;i<bytes.Length;i++)bytes[i]=(byte)(i%251); File.WriteAllBytes(fixture,bytes);
  string empty=Path.Combine(Config.BaseDir,"empty.bin"), html=Path.Combine(Config.BaseDir,"unsafe.html"), svg=Path.Combine(Config.BaseDir,"unsafe.svg"), png=Path.Combine(Config.BaseDir,"photo.png");
  File.WriteAllBytes(empty,new byte[0]); File.WriteAllText(html,"<script>alert(1)</script>"); File.WriteAllText(svg,"<svg onload='alert(1)'/>"); File.WriteAllBytes(png,Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jc1sAAAAASUVORK5CYII="));
  string large=Path.Combine(Config.BaseDir,"large.bin"), missing=Path.Combine(Config.BaseDir,"missing.bin");
  using(var big=File.Create(large)) big.SetLength(67108864);
  File.WriteAllText(missing,"move this fixture after selecting it");
  add.Invoke(server,new object[]{new string[]{fixture,empty,html,svg,png,large,missing}});
  if(!server.Start())return 2;
  File.WriteAllText(Path.Combine(Config.BaseDir,"test-ready"),"ready");
  Console.WriteLine("READY isolated server");
  Thread.Sleep(Timeout.Infinite); return 0;
 }
}}
