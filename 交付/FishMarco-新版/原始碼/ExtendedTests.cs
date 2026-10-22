using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

public static class ExtendedTests {
 static void Check(bool b,string message){if(!b)throw new Exception("New actions: "+message);}
 public static void Run(){
  var drag=new Step{Type="滑鼠拖曳",Value="左鍵",X=-25,Y=40,EndX=200,EndY=300,Hold=45,Delay=10};
  var wheel=new Step{Type="滑鼠滾輪",Value="往下",X=100,Y=50,Hold=250,WheelInterval=100,WheelDelta=120};MainForm.ValidateStep(drag);MainForm.ValidateStep(wheel);
  var saved=Json.Copy(new Template{Steps=new List<Step>{drag,wheel}});MainForm.Validate(saved);Check(saved.Steps[0].EndX==200&&saved.Steps[1].WheelInterval==100,"JSON preserves endpoints and wheel timing");
  var copy=MainForm.CopyStep(drag);copy.EndY=99;Check(drag.EndY==300,"copy is independent");
  var events=new List<string>();Func<int,CancellationToken,Task> wait=(ms,ct)=>{ct.ThrowIfCancellationRequested();events.Add("wait:"+ms);return Task.FromResult(0);};
  Func<InputPlayback> playback=()=>new InputPlayback((x,y)=>events.Add("move:"+x+","+y),(k,up)=>events.Add("key:"+k+":"+up),(b,up)=>events.Add("mouse:"+b+":"+up),d=>events.Add("wheel:"+d),wait);
  using(var p=playback())p.Perform(wheel,CancellationToken.None).GetAwaiter().GetResult();Check(events.Count(e=>e=="wheel:-120")==3&&events.Contains("wait:50"),"wheel direction, cadence and duration");
  events.Clear();wheel.Hold=0;using(var p=playback())p.Perform(wheel,CancellationToken.None).GetAwaiter().GetResult();Check(events.Count(e=>e.StartsWith("wheel:"))==1&&!events.Any(e=>e.StartsWith("wait:")),"zero seconds scrolls once");
  events.Clear();using(var p=playback())p.Perform(drag,CancellationToken.None).GetAwaiter().GetResult();Check(events[0]=="move:-25,40"&&events[1]=="mouse:左鍵:False"&&events.Contains("move:200,300")&&events.Last()=="mouse:左鍵:True","drag endpoint and release");
  events.Clear();using(var stop=new CancellationTokenSource()){bool canceled=false;using(var p=new InputPlayback((x,y)=>{},(k,u)=>{},(b,u)=>events.Add("mouse:"+u),d=>{},(ms,ct)=>{stop.Cancel();ct.ThrowIfCancellationRequested();return Task.FromResult(0);})){try{p.Perform(drag,stop.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){canceled=true;}}Check(canceled&&events.SequenceEqual(new[]{"mouse:False","mouse:True"}),"cancel releases drag");}
  events.Clear();using(var p=playback()){p.Perform(new Step{Type="鍵盤按下",Value="LControlKey"},CancellationToken.None).GetAwaiter().GetResult();p.Perform(new Step{Type="滑鼠按下",Value="左鍵",X=0,Y=0},CancellationToken.None).GetAwaiter().GetResult();}Check(events.Contains("key:162:True")&&events.Last()=="mouse:左鍵:True","unfinished recorded inputs are released");
  var buffer=new RecordingBuffer();buffer.Key((int)Keys.F5,false,0);buffer.Key((int)Keys.F6,true,10);buffer.Key((int)Keys.A,true,15);buffer.Key((int)Keys.LControlKey,false,100);buffer.Mouse("左鍵",false,new Point(10,20),150);buffer.Move(new Point(20,30),180);buffer.Wheel(-120,new Point(20,30),200);buffer.Key((int)Keys.A,false,210);buffer.Key((int)Keys.A,false,220);buffer.Key((int)Keys.A,true,260);buffer.Mouse("左鍵",true,new Point(100,200),300);var recorded=buffer.Finish(350);
  MainForm.Validate(new Template{Steps=recorded});Check(recorded.First().Type=="等待"&&recorded.First().Delay==100,"initial wait retained");Check(recorded.Sum(s=>s.Delay)==350,"recording timeline preserved including trailing wait");Check(recorded.Last().Type=="鍵盤放開"&&recorded.Last().Value=="LControlKey","stop closes held modifier");Check(!recorded.Any(s=>s.Value=="F5"||s.Value=="F6"),"recording hotkeys excluded");Check(recorded.Count(s=>s.Type=="鍵盤按下"&&s.Value=="A")==2,"key repeats preserved");Check(buffer.Finish(400).Count==recorded.Count,"finish is idempotent");
  var legacy=Json.Read<Step>("{\"Type\":\"滑鼠點擊\",\"Value\":\"左鍵\"}");MainForm.ValidateStep(legacy);Check(legacy.WheelInterval==100&&legacy.WheelDelta==120,"legacy constructor defaults");
  using(var fields=new ExtendedActionFields()){fields.LoadStep(drag);var built=fields.Build();Check(built.EndX==200&&built.EndY==300&&built.Hold==45,"drag editor roundtrip");fields.LoadStep(wheel);built=fields.Build();Check(built.Value=="往下"&&built.WheelDelta==120,"wheel editor roundtrip");}
  foreach(var kind in ActionKinds.All){var changed=CellEdits.Apply(new Step{Type="等待"},1,kind);MainForm.ValidateStep(changed);}
  var batch=new BatchPatch{EndX=-300,Hold=200}.Apply(new[]{drag,MainForm.CopyStep(drag)});Check(batch.All(s=>s.EndX==-300&&s.EndY==300&&s.Hold==200)&&drag.EndX==200,"batch endpoint edits preserve originals");
  bool invalid=false;try{MainForm.ValidateStep(new Step{Type="滑鼠滾輪",Value="往上",WheelInterval=0});}catch{invalid=true;}Check(invalid,"zero wheel interval rejected");
  using(var recorder=new InputRecorder()){recorder.Start();Check(recorder.Finish().Count==0,"native hooks install and clean up without input");}
  MainForm.TestExtendedUI();
 }
}

public partial class MainForm {
 public static void TestExtendedUI(){using(var f=new MainForm()){
  var panel=f.Controls[0];f.Controls.Remove(panel);panel.Size=f.ClientSize;panel.CreateControl();panel.PerformLayout();
  foreach(string kind in new[]{"滑鼠滾輪","滑鼠拖曳","鍵盤按下"}){
   f.type.SelectedItem=kind;panel.PerformLayout();var action=f.EditorStep();ValidateStep(action);Check(action.Type==kind,"New editor selected type");
   if(kind=="滑鼠拖曳"){action.EndX=800;action.EndY=500;action.Hold=1500;f.extendedFields.LoadStep(action);Check(f.EditorStep().EndX==800&&f.EditorStep().Hold==1500,"Main drag editor fields");}
   f.AddStep(f.EditorStep());panel.PerformLayout();SavePreview(panel,kind=="滑鼠滾輪"?"preview-wheel.png":kind=="滑鼠拖曳"?"preview-drag.png":"preview-recorded-key.png");
  }
  var before=f.Current().Steps.Count;f.DuplicateSelected();Check(f.Current().Steps.Count==before+1,"Recorded event duplication");f.UndoAction();Check(f.Current().Steps.Count==before,"New action undo");
  panel.Dispose();
 }}
}
