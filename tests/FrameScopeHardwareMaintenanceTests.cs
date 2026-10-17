using System;
using System.IO;
using System.Threading;
internal static class FrameScopeHardwareMaintenanceTests {
 public static int Main(string[] args) {
  if(args.Length==2 && args[0]=="probe") {
   Thread.CurrentThread.CurrentCulture=new System.Globalization.CultureInfo("tr-TR");
   Thread.CurrentThread.CurrentUICulture=new System.Globalization.CultureInfo("tr-TR");
   using(var lease=FrameScopeHardwareServiceClient.MaintenanceLease.TryAcquire(args[1])) return lease==null?7:0;
  }
  if(args.Length==4 && args[0]=="serve") {
   using(var pipe=new System.IO.Pipes.NamedPipeServerStream(args[1],System.IO.Pipes.PipeDirection.InOut,1,System.IO.Pipes.PipeTransmissionMode.Message)) {
    File.WriteAllText(args[2],"ready");
    pipe.WaitForConnection();
    using(var reader=new StreamReader(pipe,System.Text.Encoding.UTF8,false,1024,true))
    using(var writer=new StreamWriter(pipe,new System.Text.UTF8Encoding(false),1024,true)) {
     var serializer=new System.Web.Script.Serialization.JavaScriptSerializer();
     var request=serializer.Deserialize<System.Collections.Generic.Dictionary<string,object>>(reader.ReadLine());
     if(Convert.ToString(request["method"])!="stop")return 9;
     writer.WriteLine(serializer.Serialize(new {id=request["id"],ok=true,result=new {status="stopped",shutdownComplete=args[3]=="true"}}));
     writer.Flush();
    }
   }
   return 0;
  }
  var root=AppDomain.CurrentDomain.BaseDirectory;
  using(var lease=FrameScopeHardwareServiceClient.MaintenanceLease.TryAcquire(root)) {
   if(lease==null) throw new Exception("fixture lease missing");
   Exception error=null;
   var worker=new Thread(delegate() { try {
    using(var other=FrameScopeHardwareServiceClient.MaintenanceLease.TryAcquire(root)) if(other!=null) throw new Exception("maintenance bypass");
    FrameScopeHardwareServiceClient.EnsureServiceRunning();
    if(FrameScopeHardwareServiceClient.QueryOverlayData()!=null) throw new Exception("maintenance must return unavailable");
   } catch(Exception ex){error=ex;} });
   worker.Start(); if(!worker.Join(6000)) throw new Exception("maintenance request exceeded its bounded wait");
   if(error!=null)throw error;
  }
  using(var lease=FrameScopeHardwareServiceClient.MaintenanceLease.TryAcquire(root)) if(lease==null) throw new Exception("lease not restored");
  Exception queueError=null; Thread queued; var ready=new ManualResetEvent(false);
  using(var reader=FrameScopeHardwareServiceClient.MaintenanceLease.TryAcquire(root)) {
   queued=new Thread(delegate(){try{ready.Set();using(var other=FrameScopeHardwareServiceClient.MaintenanceLease.TryAcquire(root,1000)) if(other==null) throw new Exception("ordinary reader was rejected instead of queued");}catch(Exception ex){queueError=ex;}});
   queued.Start();ready.WaitOne();Thread.Sleep(100);
  }
  if(!queued.Join(2000)) throw new Exception("ordinary request exceeded its wait budget");
  if(queueError!=null)throw queueError;
  ready.Dispose();
  Console.WriteLine("PASS C# maintenance blocked requests and restored lease");
  return 0;
 }
}
