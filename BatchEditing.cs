using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

public class SecondsInput:NumericUpDown {
 protected override void UpdateEditText(){Text=Value.ToString("0.0##",CultureInfo.CurrentCulture);}
}
public static class WaitUnits {
 public static NumericUpDown Control(){return new SecondsInput{Minimum=0,Maximum=86400,DecimalPlaces=3,Increment=.1m,Value=1,Width=110};}
 public static int ToMilliseconds(decimal seconds){decimal ms=seconds*1000;if(ms<0||ms>86400000||ms!=decimal.Truncate(ms))throw new Exception("動作後等待須為 0～86400 sec，最多三位小數。");return (int)ms;}
 public static int Parse(string text){decimal seconds;if(!decimal.TryParse(text,NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.CurrentCulture,out seconds))throw new Exception("請輸入有效秒數，例如 1 或 0.5。");return ToMilliseconds(seconds);}
 public static Color RowColor(string type){return type=="滑鼠點擊"?Color.FromArgb(230,243,255):type=="鍵盤按壓"?Color.FromArgb(233,247,235):Color.FromArgb(255,245,218);}
}
public class BatchPatch {
 public int? X,Y,EndX,EndY,WheelDelta,Hold,Delay;public bool SetValue,SetNotes;public string Value,Notes;
 public bool HasChanges{get{return X.HasValue||Y.HasValue||EndX.HasValue||EndY.HasValue||WheelDelta.HasValue||Hold.HasValue||Delay.HasValue||SetValue||SetNotes;}}
 public List<Step> Apply(IList<Step> source){
  if(source.Count<2||source.Any(s=>s==null)||source.Select(s=>s.Type).Distinct().Count()!=1)throw new Exception("請選取至少兩個相同類型的動作。");
  string type=source[0].Type;if((EndX.HasValue||EndY.HasValue)&&type!="滑鼠拖曳")throw new Exception("只有拖曳可以修改終點。");if((WheelDelta.HasValue)&&type!="滑鼠滾輪")throw new Exception("只有滾輪可以修改捲動設定。");if((X.HasValue||Y.HasValue)&&!ActionKinds.Position(type))throw new Exception("只有滑鼠動作可以修改座標。");if(!ActionKinds.Timed(type)&&Hold.HasValue||type=="等待"&&SetValue)throw new Exception("等待動作沒有按住時間或按鍵。");
  var result=new List<Step>();foreach(var item in source){var s=MainForm.CopyStep(item);if(X.HasValue)s.X=X.Value;if(Y.HasValue)s.Y=Y.Value;if(EndX.HasValue)s.EndX=EndX.Value;if(EndY.HasValue)s.EndY=EndY.Value;if(WheelDelta.HasValue)s.WheelDelta=WheelDelta.Value;if(Hold.HasValue)s.Hold=Hold.Value;if(Delay.HasValue)s.Delay=Delay.Value;if(SetValue)s.Value=Value;if(SetNotes)s.Notes=Notes??"";RecordedGesture.Preserve(item,s);MainForm.ValidateStep(s);result.Add(s);}return result;
 }
}
public class BatchEditor:Form {
 readonly IList<Step> source;readonly Dictionary<string,CheckBox> enabled=new Dictionary<string,CheckBox>();
 readonly NumericUpDown endX=Num(-100000,100000),endY=Num(-100000,100000),wheelDelta=Num(1,12000); readonly NumericUpDown x=Num(-100000,100000),y=Num(-100000,100000),hold=Num(0,600000),delay=WaitUnits.Control();
 readonly ComboBox mouse=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=150};readonly KeyPicker key=new KeyPicker();readonly TextBox notes=new TextBox{Width=640,Height=60,Multiline=true,ScrollBars=ScrollBars.Vertical,MaxLength=int.MaxValue};
 public List<Step> Result{get;private set;}
 public BatchEditor(IList<Step> steps){
  new BatchPatch().Apply(steps);source=steps;var first=steps[0];Text="批次修改 "+steps.Count+" 個「"+first.Type+"」動作";Size=new Size(800,680);MinimumSize=new Size(800,640);StartPosition=FormStartPosition.CenterParent;Font=new Font("Microsoft JhengHei UI",10);
  var layout=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoScroll=true,Padding=new Padding(16)};Controls.Add(layout);layout.Controls.Add(new Label{Text="只套用有勾選的欄位；未勾選的內容各自保留。\n欄位初始值取自第一個動作。取消不會變更任何動作。",AutoSize=true,Margin=new Padding(3,3,3,15)});
  endX.Value=first.EndX;endY.Value=first.EndY;wheelDelta.Value=first.WheelDelta;x.Value=first.X;y.Value=first.Y;hold.Value=first.Hold;delay.Value=first.Delay/1000m;notes.Text=first.Notes??"";
  if(ActionKinds.Position(first.Type)){mouse.Items.AddRange(first.Type=="滑鼠滾輪"?new object[]{"往上","往下"}:new object[]{"左鍵","右鍵","中鍵"});mouse.SelectedItem=first.Value;Field(layout,"X","X 座標",x);Field(layout,"Y","Y 座標",y);if(ActionKinds.Button(first.Type)||first.Type=="滑鼠滾輪")Field(layout,"Value","滑鼠按鈕／方向",mouse);}else if(ActionKinds.Key(first.Type)){key.Value=first.Value;Field(layout,"Value","按鍵／組合鍵",key);}
  if(first.Type=="滑鼠拖曳"){Field(layout,"EndX","終點 X",endX);Field(layout,"EndY","終點 Y",endY);}if(first.Type=="滑鼠滾輪"){Field(layout,"WheelDelta","捲動量（120＝一格）",wheelDelta);}if(ActionKinds.Timed(first.Type))Field(layout,"Hold","持續時間（ms）",hold);Field(layout,"Delay","動作後等待（sec）",delay);Field(layout,"Notes","備註（勾選後留空＝清空）",notes);
  var actions=new FlowLayoutPanel{Width=740,Height=45};var save=new Button{Text="套用至 "+steps.Count+" 個動作",AutoSize=true};var cancel=new Button{Text="取消",AutoSize=true,DialogResult=DialogResult.Cancel};actions.Controls.Add(save);actions.Controls.Add(cancel);layout.Controls.Add(actions);CancelButton=cancel;
  save.Click+=(s,e)=>{try{ValidateChildren();var patch=new BatchPatch{EndX=On("EndX")?(int?)endX.Value:null,EndY=On("EndY")?(int?)endY.Value:null,WheelDelta=On("WheelDelta")?(int?)wheelDelta.Value:null,X=On("X")?(int?)x.Value:null,Y=On("Y")?(int?)y.Value:null,Hold=On("Hold")?(int?)hold.Value:null,Delay=On("Delay")?(int?)WaitUnits.ToMilliseconds(delay.Value):null,SetValue=On("Value"),Value=ActionKinds.Position(first.Type)?mouse.Text:key.Value,SetNotes=On("Notes"),Notes=notes.Text};if(!patch.HasChanges)throw new Exception("請勾選至少一個要修改的欄位。");Result=patch.Apply(source);DialogResult=DialogResult.OK;Close();}catch(Exception ex){MessageBox.Show(this,ex.Message,"批次修改");}};
 }
 static NumericUpDown Num(int min,int max){return new NumericUpDown{Minimum=min,Maximum=max,Width=120};}
 bool On(string name){return enabled.ContainsKey(name)&&enabled[name].Checked;}
 void Field(FlowLayoutPanel layout,string name,string label,Control control){var check=new CheckBox{Text=label,AutoSize=true};enabled[name]=check;layout.Controls.Add(check);control.Enabled=false;check.CheckedChanged+=(s,e)=>control.Enabled=check.Checked;layout.Controls.Add(control);}
}
