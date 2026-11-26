using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

// Input transitions are recording data inside a gesture, never separate action types.
public class RecordedScreen {
 public string Device {get;set;}
 public int Left {get;set;} public int Top {get;set;}
 public int Width {get;set;} public int Height {get;set;}
 public static RecordedScreen[] Current(){
  return Screen.AllScreens.Select(s=>new RecordedScreen{Device=s.DeviceName,Left=s.Bounds.Left,Top=s.Bounds.Top,Width=s.Bounds.Width,Height=s.Bounds.Height}).ToArray();
 }
 public bool Contains(int x,int y){return x>=Left&&y>=Top&&(long)x-Left<Width&&(long)y-Top<Height;}
 public static Point Resolve(RecordedScreen[] recorded,int x,int y,RecordedScreen[] current,bool requireScreen=false){
  var source=recorded==null?null:recorded.FirstOrDefault(s=>s.Contains(x,y));
  if(source==null)return new Point(x,y);
  var target=current.FirstOrDefault(s=>s.Device==source.Device);
  if(target==null){
   if(requireScreen)throw new InvalidOperationException("找不到錄製時使用的螢幕："+source.Device);
   return new Point(x,y);
  }
  return new Point(target.Left+Scale(x-source.Left,source.Width,target.Width),target.Top+Scale(y-source.Top,source.Height,target.Height));
 }
 static int Scale(int offset,int before,int after){return before<=1?0:(int)Math.Round(offset*(after-1d)/(before-1d));}
}

public class RecordedInput {
 public string Kind {get;set;} public int At {get;set;} public int Code {get;set;}
 public string Button {get;set;} public int X {get;set;} public int Y {get;set;} public int Delta {get;set;}
}

public static class RecordedGesture {
 public static bool Has(Step s){return s.RecordedInputs!=null&&s.RecordedInputs.Count>0;}
 public static void Preserve(Step original,Step edited){
  edited.Screens=original.Type==edited.Type&&original.X==edited.X&&original.Y==edited.Y&&original.EndX==edited.EndX&&original.EndY==edited.EndY?original.Screens:null;
  edited.RecordedInputs=original.Type==edited.Type&&original.Value==edited.Value&&original.X==edited.X&&original.Y==edited.Y&&original.EndX==edited.EndX&&original.EndY==edited.EndY&&original.WheelDelta==edited.WheelDelta?Json.Copy(original.RecordedInputs):null;
 }
 public static void Validate(Step s){
  if(s.Screens!=null&&s.Screens.Any(m=>m==null||string.IsNullOrEmpty(m.Device)||m.Width<=0||m.Height<=0||Math.Abs((long)m.Left)>100000||Math.Abs((long)m.Top)>100000||m.Width>200001||m.Height>200001))throw new Exception("錄製螢幕資料無效。");
  if(!Has(s))return;if(s.Type=="等待"||s.RecordedInputs.Count>10000)throw new Exception("錄製動作資料無效。");
  int previous=-1;var keys=new HashSet<int>();var buttons=new HashSet<string>();
  foreach(var e in s.RecordedInputs){if(e==null||e.At<previous||e.At<0||e.At>600000||Math.Abs((long)e.X)>100000||Math.Abs((long)e.Y)>100000)throw new Exception("錄製動作時間或座標無效。");previous=e.At;
   switch(e.Kind){
    case "KeyDown":case "KeyUp":if(e.Code<8||e.Code>254||RecordingBuffer.Reserved(e.Code))throw new Exception("錄製按鍵無效。");if(e.Kind=="KeyDown")keys.Add(e.Code);else if(!keys.Remove(e.Code))throw new Exception("錄製按鍵缺少按下事件。");break;
    case "MouseDown":case "MouseUp":if(!new[]{"左鍵","右鍵","中鍵"}.Contains(e.Button))throw new Exception("錄製滑鼠按鈕無效。");if(e.Kind=="MouseDown")buttons.Add(e.Button);else if(!buttons.Remove(e.Button))throw new Exception("錄製滑鼠缺少按下事件。");break;
    case "Move":break;case "Wheel":if(e.Delta==0||Math.Abs((long)e.Delta)>12000)throw new Exception("錄製滾輪資料無效。");break;
    default:throw new Exception("錄製動作資料無效。");
   }
  }
  if(keys.Count>0||buttons.Count>0)throw new Exception("錄製動作缺少放開事件。");
 }
 public static Step Build(List<RecordedInput> input){
  int start=input[0].At,end=input[input.Count-1].At;var firstMouse=input.FirstOrDefault(e=>e.Kind=="MouseDown");var firstWheel=input.FirstOrDefault(e=>e.Kind=="Wheel");
  var s=new Step{Hold=end-start,RecordedInputs=Json.Copy(input)};foreach(var e in s.RecordedInputs)e.At-=start;
  if(firstMouse!=null){var lastMouse=input.Last(e=>e.Kind=="MouseUp");bool drag=firstMouse.Button=="左鍵"&&input.Any(e=>(e.Kind=="Move"||e.Kind=="MouseUp")&&(Math.Abs(e.X-firstMouse.X)>=4||Math.Abs(e.Y-firstMouse.Y)>=4));s.Type=drag?"滑鼠拖曳":"滑鼠點擊";s.Value=firstMouse.Button;s.X=firstMouse.X;s.Y=firstMouse.Y;if(drag){s.EndX=lastMouse.X;s.EndY=lastMouse.Y;}}
  else if(input.Any(e=>e.Kind=="KeyDown")){s.Type="鍵盤按壓";s.Value=string.Join("+",input.Where(e=>e.Kind=="KeyDown").Select(e=>e.Code).Distinct().Select(k=>((Keys)k).ToString()));}
  else{s.Type="滑鼠滾輪";s.Value=firstWheel.Delta>0?"往上":"往下";s.WheelDelta=Math.Abs(firstWheel.Delta);s.X=firstWheel.X;s.Y=firstWheel.Y;}
  bool mixed=input.Any(e=>e.Kind=="KeyDown")&&input.Any(e=>e.Kind=="MouseDown"||e.Kind=="Wheel");
  s.Notes=mixed?"錄製：含鍵鼠組合操作":"錄製";
  // Simple clicks, single keys and individual wheel ticks need no extra event data.
  if(input.Count==2&&input[0].Kind=="MouseDown"&&input[1].Kind=="MouseUp"&&s.Type=="滑鼠點擊"||input.Count==2&&input[0].Kind=="KeyDown"&&input[1].Kind=="KeyUp"||input.Count==1&&input[0].Kind=="Wheel")s.RecordedInputs=null;
  return s;
 }
}

public class RecordingBuffer {
 readonly RecordedScreen[] screens;
 public RecordingBuffer():this(RecordedScreen.Current()){}
 internal RecordingBuffer(RecordedScreen[] screens){this.screens=screens;}
 readonly List<RecordedInput> events=new List<RecordedInput>();readonly HashSet<int> keys=new HashSet<int>();readonly HashSet<string> buttons=new HashSet<string>();
 Point position;long lastMove=-20;bool finished;List<Step> result;
 public const int MaxEvents=9700;public int Count{get{return events.Count;}}public bool Full{get{return Count>=MaxEvents;}}
 public static bool Reserved(int k){return k==(int)Keys.F5||k==(int)Keys.F6||k==(int)Keys.F9||k==(int)Keys.F10;}
 void Add(RecordedInput e,long time){e.At=(int)Math.Max(events.Count==0?0:events[events.Count-1].At,Math.Min(600000,time));events.Add(e);}
 public void Key(int code,bool up,long time){if(finished||Full||Reserved(code)||code<8||code>254)return;if(up){if(!keys.Remove(code))return;}else keys.Add(code);Add(new RecordedInput{Kind=up?"KeyUp":"KeyDown",Code=code},time);}
 public void Mouse(string button,bool up,Point p,long time){if(finished||Full)return;position=p;if(up){if(!buttons.Remove(button))return;}else buttons.Add(button);Add(new RecordedInput{Kind=up?"MouseUp":"MouseDown",Button=button,X=p.X,Y=p.Y},time);}
 public void Move(Point p,long time){if(finished||Full)return;position=p;if(buttons.Count==0||time-lastMove<15)return;lastMove=time;Add(new RecordedInput{Kind="Move",X=p.X,Y=p.Y},time);}
 public void Wheel(int delta,Point p,long time){if(finished||Full||delta==0)return;position=p;Add(new RecordedInput{Kind="Wheel",Delta=Math.Sign(delta)*Math.Min(12000,Math.Abs(delta)),X=p.X,Y=p.Y},time);}
 public List<Step> Finish(long time){if(finished)return result.Select(MainForm.CopyStep).ToList();time=Math.Min(600000,time);
  foreach(int k in keys.ToArray())Add(new RecordedInput{Kind="KeyUp",Code=k},time);foreach(string b in buttons.ToArray())Add(new RecordedInput{Kind="MouseUp",Button=b,X=position.X,Y=position.Y},time);
  keys.Clear();buttons.Clear();result=new List<Step>();var gesture=new List<RecordedInput>();int lastEnd=0;
  foreach(var e in events){if(gesture.Count==0){int gap=e.At-lastEnd;if(result.Count>0)result[result.Count-1].Delay=gap;}
   gesture.Add(e);if(e.Kind=="KeyDown")keys.Add(e.Code);if(e.Kind=="KeyUp")keys.Remove(e.Code);if(e.Kind=="MouseDown")buttons.Add(e.Button);if(e.Kind=="MouseUp")buttons.Remove(e.Button);
   if(keys.Count==0&&buttons.Count==0){var step=RecordedGesture.Build(gesture);
    var used=screens.Where(m=>gesture.Any(input=>input.Kind!="KeyDown"&&input.Kind!="KeyUp"&&m.Contains(input.X,input.Y))).ToArray();
    if(used.Length>0)step.Screens=used;
    result.Add(step);lastEnd=e.At;gesture=new List<RecordedInput>();}
  }
  if(result.Count>0)result[result.Count-1].Delay=(int)Math.Max(0,time-lastEnd);finished=true;return result.Select(MainForm.CopyStep).ToList();
 }
}
