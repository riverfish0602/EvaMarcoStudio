using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

public static class ActionKinds {
 public static readonly string[] All={"滑鼠點擊","滑鼠滾輪","滑鼠拖曳","鍵盤按壓","等待"};
 public static bool Position(string t){return t.StartsWith("滑鼠");}
 public static bool Key(string t){return t.StartsWith("鍵盤");}
 public static bool Button(string t){return t=="滑鼠點擊";}
 public static bool Timed(string t){return t=="滑鼠點擊"||t=="鍵盤按壓"||t=="滑鼠滾輪"||t=="滑鼠拖曳";}
 public static bool Extended(string t){return t!="滑鼠點擊"&&t!="鍵盤按壓"&&t!="等待";}
 public static string DefaultValue(string t){return Key(t)?"Space":Button(t)||t=="滑鼠拖曳"?"左鍵":t=="滑鼠滾輪"?"往上":"";}
 public static void Validate(Step s){
  if(Math.Abs((long)s.EndX)>100000||Math.Abs((long)s.EndY)>100000)throw new Exception("終點座標超出範圍。");
  if(Key(s.Type))MainForm.ParseKeys(s.Value);
  if(Button(s.Type)&&!new[]{"左鍵","右鍵","中鍵"}.Contains(s.Value))throw new Exception("滑鼠按鈕無效。");
  if(s.Type=="滑鼠拖曳"&&s.Value!="左鍵")throw new Exception("拖曳使用左鍵。");
  if(s.Type=="滑鼠滾輪"&&(!new[]{"往上","往下"}.Contains(s.Value)||s.WheelDelta<1||s.WheelDelta>12000))throw new Exception("滾輪方向或捲動量無效。");
  RecordedGesture.Validate(s);
  if(s.Type!="等待"&&!Timed(s.Type)&&s.Hold!=0)throw new Exception("此動作請使用動作後等待，持續時間必須為 0。");
 }
}

// Every run owns its held inputs. Cancellation and failures release all tracked inputs.
public sealed class InputPlayback:IDisposable {
 readonly List<ushort> keys=new List<ushort>();readonly List<string> buttons=new List<string>();
 readonly Action<int,int> move;readonly Action<ushort,bool> key;readonly Action<string,bool> mouse;readonly Action<int> wheel;readonly Func<int,CancellationToken,Task> wait;
 public InputPlayback():this(Native.Move,Native.Key,Native.Mouse,Native.Wheel,(ms,ct)=>Task.Delay(ms,ct)){}
 public InputPlayback(Action<int,int> m,Action<ushort,bool> k,Action<string,bool> b,Action<int> w,Func<int,CancellationToken,Task> pause){move=m;key=k;mouse=b;wheel=w;wait=pause;}
 void Key(ushort k,bool up){key(k,up);if(up)keys.Remove(k);else if(!keys.Contains(k))keys.Add(k);}
 void Button(string b,bool up){mouse(b,up);if(up)buttons.Remove(b);else if(!buttons.Contains(b))buttons.Add(b);}
 public async Task Perform(Step s,CancellationToken token){
  token.ThrowIfCancellationRequested();
  if(RecordedGesture.Has(s)){await PlayRecorded(s,token);return;}
  if(ActionKinds.Position(s.Type))move(s.X,s.Y);
  switch(s.Type){
   case "滑鼠點擊":Button(s.Value,false);try{await wait(s.Hold,token);}finally{Button(s.Value,true);}break;
   case "鍵盤按壓":
    var pressed=new List<ushort>();try{foreach(var k in MainForm.ParseKeys(s.Value)){Key(k,false);pressed.Add(k);}await wait(s.Hold,token);}finally{foreach(var k in pressed.AsEnumerable().Reverse())Key(k,true);}break;
   case "滑鼠滾輪":
    wheel(s.Value=="往上"?s.WheelDelta:-s.WheelDelta);if(s.Hold>0)await wait(s.Hold,token);break;
   case "滑鼠拖曳":
    Button("左鍵",false);try{int t=0;while(t<s.Hold){int pause=Math.Min(15,s.Hold-t);await wait(pause,token);t+=pause;double fraction=(double)t/s.Hold;move((int)Math.Round(s.X+(s.EndX-s.X)*fraction),(int)Math.Round(s.Y+(s.EndY-s.Y)*fraction));}token.ThrowIfCancellationRequested();move(s.EndX,s.EndY);}finally{Button("左鍵",true);}break;
  }
 }
 async Task PlayRecorded(Step s,CancellationToken token){
  int original=s.RecordedInputs[s.RecordedInputs.Count-1].At,elapsed=0;
  try{foreach(var e in s.RecordedInputs){int at=original==0?0:(int)((long)e.At*s.Hold/original);if(at>elapsed)await wait(at-elapsed,token);elapsed=at;token.ThrowIfCancellationRequested();
    switch(e.Kind){case "KeyDown":Key((ushort)e.Code,false);break;case "KeyUp":Key((ushort)e.Code,true);break;case "MouseDown":move(e.X,e.Y);Button(e.Button,false);break;case "MouseUp":move(e.X,e.Y);Button(e.Button,true);break;case "Move":move(e.X,e.Y);break;case "Wheel":move(e.X,e.Y);wheel(e.Delta);break;}
   }if(s.Hold>elapsed)await wait(s.Hold-elapsed,token);
  }finally{Dispose();}
 }
 public void Dispose(){Exception error=null;foreach(var k in keys.ToArray().Reverse())try{Key(k,true);}catch(Exception ex){error=ex;}foreach(var b in buttons.ToArray().Reverse())try{Button(b,true);}catch(Exception ex){error=ex;}if(error!=null)throw new Exception("釋放輸入失敗："+error.Message,error);}
}

// Coordinate and direction editor only. All timing is edited in the action list.
public class ExtendedActionFields:FlowLayoutPanel {
 readonly NumericUpDown x=Number(),y=Number(),endX=Number(),endY=Number();
 readonly ComboBox direction=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=110};
 readonly FlowLayoutPanel positionRow=Row(),endRow=Row(),settings=Row();string kind="滑鼠滾輪";Step loaded;
 public bool Dragging{get{return Controls.OfType<FlowLayoutPanel>().SelectMany(p=>p.Controls.OfType<CrosshairDrag>()).Any(c=>c.Dragging);}}
 public ExtendedActionFields(){AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;FlowDirection=FlowDirection.TopDown;WrapContents=false;Margin=Padding.Empty;
  Field(positionRow,"起點 X",x);Field(positionRow,"Y",y);positionRow.Controls.Add(new CrosshairDrag(()=>new Point((int)x.Value,(int)y.Value),p=>{x.Value=p.X;y.Value=p.Y;}));Controls.Add(positionRow);
  Field(endRow,"終點 X",endX);Field(endRow,"Y",endY);endRow.Controls.Add(new CrosshairDrag(()=>new Point((int)endX.Value,(int)endY.Value),p=>{endX.Value=p.X;endY.Value=p.Y;}));Controls.Add(endRow);
  direction.Items.AddRange(new object[]{"往上","往下"});direction.SelectedIndex=0;Field(settings,"滾輪方向",direction);Controls.Add(settings);
  Controls.Add(new Label{AutoSize=true,ForeColor=Color.DimGray,Text="每列執行一個動作；持續時間與動作後等待請在下方清單修改。",Margin=new Padding(0,8,0,8)});SetKind(kind);
 }
 static NumericUpDown Number(){return new NumericUpDown{Minimum=-100000,Maximum=100000,Width=90};}
 static FlowLayoutPanel Row(){return new FlowLayoutPanel{AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=true,MaximumSize=new Size(1050,0),Margin=Padding.Empty};}
 static void Field(FlowLayoutPanel row,string title,Control c){var panel=new FlowLayoutPanel{AutoSize=true,WrapContents=false,Margin=new Padding(0,3,12,3)};panel.Controls.Add(new Label{Text=title,AutoSize=true,Margin=new Padding(0,6,5,0)});panel.Controls.Add(c);row.Controls.Add(panel);}
 public void SetKind(string t){kind=t;loaded=null;positionRow.Visible=ActionKinds.Position(t);endRow.Visible=t=="滑鼠拖曳";settings.Visible=t=="滑鼠滾輪";}
 public void LoadStep(Step s){SetKind(s.Type);loaded=MainForm.CopyStep(s);x.Value=s.X;y.Value=s.Y;endX.Value=s.EndX;endY.Value=s.EndY;if(direction.Items.Contains(s.Value))direction.SelectedItem=s.Value;}
 public Step Build(){var s=new Step{Type=kind,X=(int)x.Value,Y=(int)y.Value,EndX=kind=="滑鼠拖曳"?(int)endX.Value:0,EndY=kind=="滑鼠拖曳"?(int)endY.Value:0,Hold=loaded==null?(kind=="滑鼠拖曳"?500:0):loaded.Hold,Value=kind=="滑鼠拖曳"?"左鍵":direction.Text,WheelDelta=loaded==null?120:loaded.WheelDelta};if(loaded!=null)RecordedGesture.Preserve(loaded,s);return s;}
}
public class ExtendedStepEditor:Form {
 readonly ExtendedActionFields fields=new ExtendedActionFields();readonly NumericUpDown delay=WaitUnits.Control();readonly TextBox notes=new TextBox{Width=850,Multiline=true,Height=65};readonly Step source;
 public Step Result{get;private set;}
 public ExtendedStepEditor(Step s){source=s;Text="修改「"+s.Type+"」";Size=new Size(1120,520);MinimumSize=new Size(900,460);StartPosition=FormStartPosition.CenterParent;Font=new Font("Microsoft JhengHei UI",10);var layout=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(16)};Controls.Add(layout);fields.LoadStep(s);layout.Controls.Add(fields);if(RecordedGesture.Has(s))layout.Controls.Add(new Label{AutoSize=true,ForeColor=Color.DarkOrange,Text="此動作包含錄製路徑或組合操作；修改座標／方向會以新設定重新建立動作。"});layout.Controls.Add(new Label{Text="動作後等待（sec）",AutoSize=true});delay.Value=s.Delay/1000m;layout.Controls.Add(delay);layout.Controls.Add(new Label{Text="備註",AutoSize=true});notes.Text=s.Notes??"";layout.Controls.Add(notes);var row=new FlowLayoutPanel{AutoSize=true};var save=new Button{Text="儲存修改",AutoSize=true};var cancel=new Button{Text="取消",DialogResult=DialogResult.Cancel,AutoSize=true};row.Controls.Add(save);row.Controls.Add(cancel);layout.Controls.Add(row);CancelButton=cancel;save.Click+=(o,e)=>{try{ValidateChildren();Result=fields.Build();Result.Sequence=source.Sequence;Result.Delay=WaitUnits.ToMilliseconds(delay.Value);Result.Notes=notes.Text;MainForm.ValidateStep(Result);DialogResult=DialogResult.OK;}catch(Exception ex){MessageBox.Show(this,ex.Message);}};}
}
