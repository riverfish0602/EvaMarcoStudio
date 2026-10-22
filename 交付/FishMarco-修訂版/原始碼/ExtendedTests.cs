using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

public static class ExtendedTests {
 static void Check(bool b,string message){if(!b)throw new Exception("New actions: "+message);}
 public static void Run(){
  Check(ActionKinds.All.SequenceEqual(new[]{"滑鼠點擊","鍵盤按壓","等待","滑鼠滾輪","滑鼠拖曳"}),"only five complete action types");
  foreach(var removed in new[]{"滑鼠移動","滑鼠按下","滑鼠放開","鍵盤按下","鍵盤放開"}){bool rejected=false;try{MainForm.ValidateStep(new Step{Type=removed});}catch{rejected=true;}Check(rejected,"removed action rejected: "+removed);}
  var drag=new Step{Type="滑鼠拖曳",Value="左鍵",X=-25,Y=40,EndX=200,EndY=300,Hold=45,Delay=10};
  var wheel=new Step{Type="滑鼠滾輪",Value="往下",X=100,Y=50,Hold=250,WheelDelta=120};MainForm.ValidateStep(drag);MainForm.ValidateStep(wheel);
  var saved=Json.Copy(new Template{Steps=new List<Step>{drag,wheel}});MainForm.Validate(saved);Check(saved.Steps[0].EndX==200&&saved.Steps[1].WheelDelta==120,"JSON preserves action settings");
  var copy=MainForm.CopyStep(drag);copy.EndY=99;Check(drag.EndY==300,"copy is independent");
  var events=new List<string>();Func<int,CancellationToken,Task> wait=(ms,ct)=>{ct.ThrowIfCancellationRequested();events.Add("wait:"+ms);return Task.FromResult(0);};
  Func<InputPlayback> playback=()=>new InputPlayback((x,y)=>events.Add("move:"+x+","+y),(k,up)=>events.Add("key:"+k+":"+up),(b,up)=>events.Add("mouse:"+b+":"+up),d=>events.Add("wheel:"+d),wait);
  using(var p=playback())p.Perform(wheel,CancellationToken.None).GetAwaiter().GetResult();Check(events.Count(e=>e=="wheel:-120")==1&&events.Contains("wait:250"),"one scroll per row, never a timed repeat");
  events.Clear();wheel.Hold=0;using(var p=playback())p.Perform(wheel,CancellationToken.None).GetAwaiter().GetResult();Check(events.Count(e=>e.StartsWith("wheel:"))==1&&!events.Any(e=>e.StartsWith("wait:")),"zero duration scrolls once");
  events.Clear();using(var p=playback())p.Perform(drag,CancellationToken.None).GetAwaiter().GetResult();Check(events[0]=="move:-25,40"&&events[1]=="mouse:左鍵:False"&&events.Contains("move:200,300")&&events.Last()=="mouse:左鍵:True","drag endpoint and release");
  events.Clear();using(var stop=new CancellationTokenSource()){bool canceled=false;using(var p=new InputPlayback((x,y)=>{},(k,u)=>{},(b,u)=>events.Add("mouse:"+u),d=>{},(ms,ct)=>{stop.Cancel();ct.ThrowIfCancellationRequested();return Task.FromResult(0);})){try{p.Perform(drag,stop.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){canceled=true;}}Check(canceled&&events.SequenceEqual(new[]{"mouse:False","mouse:True"}),"cancel releases drag");}
  var buffer=new RecordingBuffer();buffer.Key((int)Keys.F5,false,0);buffer.Key((int)Keys.F6,true,10);buffer.Key((int)Keys.A,true,15);buffer.Key((int)Keys.LControlKey,false,100);buffer.Mouse("左鍵",false,new Point(10,20),150);buffer.Move(new Point(20,30),180);buffer.Wheel(-120,new Point(20,30),200);buffer.Key((int)Keys.A,false,210);buffer.Key((int)Keys.A,false,220);buffer.Key((int)Keys.A,true,260);buffer.Mouse("左鍵",true,new Point(100,200),300);var recorded=buffer.Finish(350);
  MainForm.Validate(new Template{Steps=recorded});Check(recorded.Count==2&&recorded.First().Type=="等待"&&recorded.Last().Type=="滑鼠拖曳","overlapping input is one gesture, not raw rows");Check(recorded.Sum(s=>s.Delay+s.Hold)==350,"gesture durations plus waits preserve timeline");
  var gesture=recorded.Last();Check(gesture.RecordedInputs.Last().Kind=="KeyUp"&&gesture.RecordedInputs.Last().Code==(int)Keys.LControlKey,"stop closes held modifier");Check(gesture.RecordedInputs.Count(e=>e.Kind=="KeyDown"&&e.Code==(int)Keys.A)==2,"key repeats preserved inside gesture");Check(buffer.Finish(400).Count==recorded.Count,"finish is idempotent");
  events.Clear();using(var p=playback())p.Perform(gesture,CancellationToken.None).GetAwaiter().GetResult();Check(events[0]=="key:162:False"&&events.Last()=="key:162:True"&&events.Contains("wheel:-120")&&events.Contains("move:20,30"),"compound gesture preserves modifier, wheel and drag path");
  events.Clear();using(var stop=new CancellationTokenSource()){bool canceled=false;using(var p=new InputPlayback((x,y)=>{},(k,u)=>events.Add("key:"+k+":"+u),(b,u)=>events.Add("mouse:"+u),d=>{},(ms,ct)=>{stop.Cancel();ct.ThrowIfCancellationRequested();return Task.FromResult(0);})){try{p.Perform(gesture,stop.Token).GetAwaiter().GetResult();}catch(OperationCanceledException){canceled=true;}}Check(canceled&&events.Last()=="key:162:True","cancel compound gesture releases modifier");}
  Check(RecordedGesture.Has(CellEdits.Apply(gesture,7,"備註修改")),"notes preserve recorded path");var sped=CellEdits.Apply(gesture,5,"500");events.Clear();using(var p=playback())p.Perform(sped,CancellationToken.None).GetAwaiter().GetResult();Check(events.Where(e=>e.StartsWith("wait:")).Sum(e=>int.Parse(e.Substring(5)))==500,"list duration rescales recording");
  var changed=new BatchPatch{EndX=700}.Apply(new[]{gesture,MainForm.CopyStep(gesture)});Check(!RecordedGesture.Has(changed[0])&&changed[0].EndX==700,"coordinate edits use new endpoint");
  var simple=new RecordingBuffer();simple.Move(new Point(9,9),10);simple.Mouse("左鍵",false,new Point(10,20),100);simple.Mouse("左鍵",true,new Point(10,20),150);simple.Key((int)Keys.A,false,200);simple.Key((int)Keys.A,true,300);simple.Wheel(120,new Point(10,20),350);var rows=simple.Finish(400);
  Check(rows.Select(s=>s.Type).SequenceEqual(new[]{"等待","滑鼠點擊","鍵盤按壓","滑鼠滾輪"}),"simple recording produces familiar actions");Check(rows.Sum(s=>s.Hold+s.Delay)==400&&rows.All(s=>!RecordedGesture.Has(s)),"folding preserves holds and idle gaps");
  var idle=new RecordingBuffer();idle.Move(new Point(20,20),50);Check(idle.Finish(100).Count==0,"standalone movement creates no action");
  var chord=new RecordingBuffer();chord.Key((int)Keys.LControlKey,false,0);chord.Key((int)Keys.C,false,10);chord.Key((int)Keys.C,true,20);chord.Key((int)Keys.LControlKey,true,30);var combo=chord.Finish(40).Single();Check(combo.Type=="鍵盤按壓"&&combo.Value=="LControlKey+C"&&combo.Hold==30&&combo.Delay==10,"chord becomes one keyboard action");MainForm.Validate(Json.Copy(new Template{Steps=new List<Step>{combo,gesture}}));
  using(var fields=new ExtendedActionFields()){fields.LoadStep(drag);var built=fields.Build();Check(built.EndX==200&&built.EndY==300&&built.Hold==45,"coordinate editor preserves duration");Check(Descendants(fields).OfType<NumericUpDown>().Count()==4,"only coordinate controls; no duration, count or interval");}
  foreach(var kind in ActionKinds.All)MainForm.ValidateStep(CellEdits.Apply(new Step{Type="等待"},1,kind));
  using(var recorder=new InputRecorder()){recorder.Start();Check(recorder.Finish().Count==0,"native hooks install and clean up without input");}
  var counts=new List<int>();CountdownOverlay.Count(CancellationToken.None,counts.Add,(ms,ct)=>{Check(ms==1000,"one second per countdown number");return Task.FromResult(0);}).GetAwaiter().GetResult();Check(counts.SequenceEqual(new[]{3,2,1}),"countdown order");
  using(var stop=new CancellationTokenSource()){counts.Clear();bool canceled=false;try{CountdownOverlay.Count(stop.Token,counts.Add,(ms,ct)=>{stop.Cancel();return Task.FromResult(0);}).GetAwaiter().GetResult();}catch(OperationCanceledException){canceled=true;}Check(canceled&&counts.SequenceEqual(new[]{3}),"cancel stops countdown");}
  using(var badge=new RecordingBadge()){badge.SetProgress(12300,50);using(var bitmap=new Bitmap(badge.Width,badge.Height)){badge.DrawToBitmap(bitmap,new Rectangle(Point.Empty,badge.Size));bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"preview-recording.png"));}}
  using(var countdown=new CountdownOverlay("即將錄製 · F6 取消"))using(var bitmap=new Bitmap(countdown.Width,countdown.Height)){countdown.DrawToBitmap(bitmap,new Rectangle(Point.Empty,countdown.Size));bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"preview-recording-countdown.png"));}
  MainForm.TestExtendedUI();
 }
 static IEnumerable<Control> Descendants(Control c){foreach(Control child in c.Controls){yield return child;foreach(var nested in Descendants(child))yield return nested;}}
}

public partial class MainForm {
 public static void TestExtendedUI(){using(var f=new MainForm()){
  var panel=f.Controls[0];f.Controls.Remove(panel);panel.Size=f.ClientSize;panel.CreateControl();panel.PerformLayout();Check(f.type.Items.Count==5,"only five actions in dropdown");
  foreach(string kind in new[]{"滑鼠滾輪","滑鼠拖曳"}){
   f.type.SelectedItem=kind;panel.PerformLayout();var action=f.EditorStep();ValidateStep(action);Check(action.Type==kind,"new editor selected type");
   if(kind=="滑鼠拖曳"){action.EndX=800;action.EndY=500;action.Hold=1500;f.extendedFields.LoadStep(action);Check(f.EditorStep().EndX==800&&f.EditorStep().Hold==1500,"drag editor preserves list settings");}
   Check(!f.hold.Visible&&!f.delay.Visible,"timing absent from wheel/drag toolbar");f.AddStep(f.EditorStep());panel.PerformLayout();SavePreview(panel,kind=="滑鼠滾輪"?"preview-wheel.png":"preview-drag.png");
  }
  var before=f.Current().Steps.Count;f.DuplicateSelected();Check(f.Current().Steps.Count==before+1,"gesture duplication");f.UndoAction();Check(f.Current().Steps.Count==before,"gesture undo");panel.Dispose();
 }}
}
