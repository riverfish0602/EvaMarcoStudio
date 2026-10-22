using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

public class KeyPicker:FlowLayoutPanel {
 readonly CaptureBox input;readonly Button capture=new Button{Text="捕捉按鍵",AutoSize=true};
 public static bool AnyRecording{get;private set;} public bool Recording{get;private set;}
 public KeyPicker(){Width=680;AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;MaximumSize=new Size(680,0);WrapContents=true;Padding=new Padding(0,4,0,6);
  input=new CaptureBox(this){Width=260,Text="Space"};Controls.Add(input);Controls.Add(capture);
  capture.Click+=(s,e)=>{if(Recording)StopCapture();else{Recording=true;AnyRecording=true;input.ReadOnly=true;capture.Text="捕捉中（Esc 取消）";input.Focus();}};
  input.LostFocus+=(s,e)=>StopCapture();SetFlowBreak(capture,true);
 }
 public string Value{get{return input.Text.Trim();}set{StopCapture();input.Text=value??"Space";}}
 public void StopCapture(){if(!Recording)return;Recording=false;AnyRecording=false;input.ReadOnly=false;capture.Text="捕捉按鍵";}
 public static string FromKeyData(Keys data){Keys code=data&Keys.KeyCode;var parts=new List<string>();if((data&Keys.Control)!=0)parts.Add("Ctrl");if((data&Keys.Alt)!=0)parts.Add("Alt");if((data&Keys.Shift)!=0)parts.Add("Shift");string name=code>=Keys.D0&&code<=Keys.D9?((int)code-48).ToString():code.ToString();parts.Add(name);return string.Join("+",parts);}
 void CaptureData(Keys data){var code=data&Keys.KeyCode;if(code==Keys.Escape){StopCapture();return;}if(code==Keys.ControlKey||code==Keys.Menu||code==Keys.ShiftKey)return;string value=FromKeyData(data);try{MainForm.ParseKeys(value);Value=value;}catch{capture.Text="此鍵保留，請按其他鍵";}}
 protected override void Dispose(bool disposing){if(disposing)StopCapture();base.Dispose(disposing);}
 class CaptureBox:TextBox {readonly KeyPicker picker;public CaptureBox(KeyPicker p){picker=p;}protected override void OnKeyDown(KeyEventArgs e){if(picker.Recording){picker.CaptureData(e.KeyData);e.Handled=true;e.SuppressKeyPress=true;return;}base.OnKeyDown(e);}protected override bool ProcessCmdKey(ref Message msg,Keys data){if(picker.Recording){picker.CaptureData(data);return true;}return base.ProcessCmdKey(ref msg,data);}}
}
public class CrosshairDrag:Control {
 readonly Func<Point> read;readonly Action<Point> write;readonly List<MarkerOverlay> overlays=new List<MarkerOverlay>();Point before;double opacity;Form form;
 public bool Dragging{get;private set;}
 public CrosshairDrag(Func<Point> get,Action<Point> set){read=get;write=set;AutoSize=true;Width=360;Height=44;Cursor=Cursors.SizeAll;BackColor=Color.FromArgb(235,243,255);SetStyle(ControlStyles.Selectable,true);TabStop=true;}
 protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);using(var pen=new Pen(Color.FromArgb(0,102,204),2)){e.Graphics.DrawLine(pen,10,20,34,20);e.Graphics.DrawLine(pen,22,8,22,32);e.Graphics.DrawEllipse(pen,16,14,12,12);}TextRenderer.DrawText(e.Graphics,"拖曳十字至目標（Esc 取消）",Font,new Rectangle(44,0,Width-48,Height),Color.Black,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix);}
 public override Size GetPreferredSize(Size proposedSize){var text=TextRenderer.MeasureText("拖曳十字至目標（Esc 取消）",Font);return new Size(text.Width+60,Math.Max(44,text.Height+16));}
 protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);Size=GetPreferredSize(Size.Empty);}
 protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;Focus();before=read();Dragging=true;form=FindForm();if(form!=null){opacity=form.Opacity;form.Opacity=.20;}Capture=true;}
 protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(!Dragging)return;var p=Cursor.Position;write(p);if(overlays.Count==0)foreach(var screen in Screen.AllScreens)overlays.Add(new MarkerOverlay(screen.Bounds));foreach(var o in overlays){o.Markers=new List<PointMarker>{new PointMarker{Position=p,Label="新增預覽",Preview=true}};if(!o.Visible)o.Show();o.Invalidate();}}
 protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);if(Dragging&&e.Button==MouseButtons.Left){var p=Cursor.Position;write(Screen.AllScreens.Any(sc=>sc.Bounds.Contains(p))?p:before);Finish(false);}}
 protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(Dragging&&!Capture)Finish(true);}
 protected override bool ProcessCmdKey(ref Message msg,Keys data){if(Dragging&&data==Keys.Escape){Finish(true);return true;}return base.ProcessCmdKey(ref msg,data);}
 void Finish(bool revert){if(!Dragging)return;Dragging=false;Capture=false;if(form!=null&&!form.IsDisposed)form.Opacity=opacity;if(revert)write(before);foreach(var o in overlays)o.Dispose();overlays.Clear();}
 protected override void Dispose(bool disposing){if(disposing)Finish(true);base.Dispose(disposing);}
}
public class StepEditor:Form {
 readonly Step source;readonly TextBox notes=new TextBox{Width=650,Height=60,Multiline=true,ScrollBars=ScrollBars.Vertical,MaxLength=int.MaxValue};
 readonly NumericUpDown x=Number(-100000,100000),y=Number(-100000,100000),hold=Number(0,600000),delay=WaitUnits.Control();
 readonly ComboBox mouse=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=160};
 readonly KeyPicker key=new KeyPicker();
 readonly Panel drag=new Panel{Width=600,Height=65,BackColor=Color.FromArgb(235,243,255),Cursor=Cursors.SizeAll,TabStop=true};
 readonly Label location=new Label{AutoSize=true};
 readonly List<MarkerOverlay> overlays=new List<MarkerOverlay>();
 bool ready,dragging;Point before;double originalOpacity,ownerOpacity;
 public Step Result {get;private set;}
 public StepEditor(Step step){
  source=step;Text="修改動作 #"+step.Sequence+" · "+step.Type;Size=new Size(730,540);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;StartPosition=FormStartPosition.CenterParent;Font=new Font("Microsoft JhengHei UI",10);KeyPreview=true;
  var layout=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(18),AutoScroll=true};Controls.Add(layout);
  layout.Controls.Add(new Label{Text="修改後按「儲存修改」套用；取消會保留原動作。",AutoSize=true,Margin=new Padding(3,3,3,15)});
  x.Value=step.X;y.Value=step.Y;hold.Value=step.Hold;delay.Value=step.Delay/1000m;
  if(step.Type=="滑鼠點擊"){
   mouse.Items.AddRange(new object[]{"左鍵","右鍵","中鍵"});mouse.SelectedItem=step.Value;
   var coordinates=Row();Field(coordinates,"X",x);Field(coordinates,"Y",y);Field(coordinates,"滑鼠按鈕",mouse);layout.Controls.Add(coordinates);
   drag.Paint+=(s,e)=>{using(var pen=new Pen(Color.FromArgb(0,102,204),3)){e.Graphics.DrawLine(pen,18,32,54,32);e.Graphics.DrawLine(pen,36,14,36,50);e.Graphics.DrawEllipse(pen,27,23,18,18);}e.Graphics.DrawString("按住此十字，拖到螢幕目標後放開（Esc 取消拖曳）",Font,Brushes.Black,68,23);};
   drag.MouseDown+=(s,e)=>{if(e.Button!=MouseButtons.Left)return;before=new Point((int)x.Value,(int)y.Value);dragging=true;originalOpacity=Opacity;ownerOpacity=Owner==null?1:Owner.Opacity;Opacity=.25;if(Owner!=null)Owner.Opacity=.20;drag.Capture=true;};
   drag.MouseMove+=(s,e)=>{if(dragging)SetPosition(Cursor.Position);};
   drag.MouseUp+=(s,e)=>{if(dragging&&e.Button==MouseButtons.Left){var p=Cursor.Position;if(Screen.AllScreens.Any(sc=>sc.Bounds.Contains(p)))SetPosition(p);else SetPosition(before);EndDrag(false);}};
   drag.MouseCaptureChanged+=(s,e)=>{if(dragging&&!drag.Capture)EndDrag(true);};layout.Controls.Add(drag);layout.Controls.Add(location);
   x.ValueChanged+=(s,e)=>Preview();y.ValueChanged+=(s,e)=>Preview();
  }else if(step.Type=="鍵盤按壓"){
   key.Value=step.Value;layout.Controls.Add(key);
  }
  var timing=Row();if(step.Type!="等待")Field(timing,"按住（ms）",hold);Field(timing,"動作後等待（sec）",delay);layout.Controls.Add(timing);
  notes.Text=step.Notes??"";layout.Controls.Add(new Label{Text="備註（選填，可輸入文字、數字或多行內容）",AutoSize=true});layout.Controls.Add(notes);var actions=Row();Button save=new Button{Text="儲存修改",AutoSize=true},cancel=new Button{Text="取消",AutoSize=true,DialogResult=DialogResult.Cancel};actions.Controls.Add(save);actions.Controls.Add(cancel);layout.Controls.Add(actions);AcceptButton=save;CancelButton=cancel;
  save.Click+=(s,e)=>{try{ValidateChildren();Result=BuildResult();MainForm.ValidateStep(Result);DialogResult=DialogResult.OK;Close();}catch(Exception ex){MessageBox.Show(this,ex.Message,"請檢查設定");}};
  KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Escape&&dragging){EndDrag(true);e.Handled=true;e.SuppressKeyPress=true;}};
  Shown+=(s,e)=>{ready=true;Preview();};FormClosing+=(s,e)=>{if(dragging)EndDrag(true);ready=false;ClearPreview();};
 }
 static NumericUpDown Number(int min,int max){return new NumericUpDown{Minimum=min,Maximum=max,Width=125};}
 static FlowLayoutPanel Row(){return new FlowLayoutPanel{Width=660,Height=45,WrapContents=false};}
 static void Field(FlowLayoutPanel row,string label,Control control){row.Controls.Add(new Label{Text=label,AutoSize=true,Margin=new Padding(3,8,5,0)});row.Controls.Add(control);}
 public void SetPosition(Point p){x.Value=Math.Max(x.Minimum,Math.Min(x.Maximum,p.X));y.Value=Math.Max(y.Minimum,Math.Min(y.Maximum,p.Y));}
 public Step BuildResult(){return new Step{Sequence=source.Sequence,Type=source.Type,X=source.Type=="滑鼠點擊"?(int)x.Value:0,Y=source.Type=="滑鼠點擊"?(int)y.Value:0,Value=source.Type=="滑鼠點擊"?mouse.Text:source.Type=="鍵盤按壓"?key.Value:"",Hold=source.Type=="等待"?0:(int)hold.Value,Delay=WaitUnits.ToMilliseconds(delay.Value),Notes=notes.Text};}
 protected override bool ProcessCmdKey(ref Message msg,Keys keyData){if(keyData==Keys.Escape&&dragging){EndDrag(true);return true;}return base.ProcessCmdKey(ref msg,keyData);}
 void EndDrag(bool revert){if(!dragging)return;dragging=false;drag.Capture=false;Opacity=originalOpacity;if(Owner!=null)Owner.Opacity=ownerOpacity;if(revert)SetPosition(before);Preview();}
 void Preview(){location.Text="目標座標：X = "+x.Value+"，Y = "+y.Value;if(!ready||source.Type!="滑鼠點擊")return;if(overlays.Count==0)foreach(var screen in Screen.AllScreens)overlays.Add(new MarkerOverlay(screen.Bounds));foreach(var overlay in overlays){overlay.Markers=new List<PointMarker>{new PointMarker{Position=new Point((int)x.Value,(int)y.Value),Label=source.Sequence+" · 修改預覽",Preview=true}};if(!overlay.Visible)overlay.Show();overlay.Invalidate();}}
 void ClearPreview(){foreach(var overlay in overlays)overlay.Dispose();overlays.Clear();}
 protected override void Dispose(bool disposing){if(disposing){if(dragging)EndDrag(true);ClearPreview();}base.Dispose(disposing);}
}

public class UnsavedChangesDialog:Form {
 public UnsavedChangesDialog(){
  Text=" ";Font=new Font("Microsoft JhengHei UI",10);AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;FormBorderStyle=FormBorderStyle.FixedDialog;StartPosition=FormStartPosition.CenterParent;MaximizeBox=false;MinimizeBox=false;ShowInTaskbar=false;
  var layout=new FlowLayoutPanel{AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(20)};Controls.Add(layout);
  layout.Controls.Add(new Label{AutoSize=true,Text="尚未儲存",Font=new Font(Font,FontStyle.Bold),Margin=new Padding(3,3,3,12)});
  layout.Controls.Add(new Label{AutoSize=true,Text="範本有尚未儲存的修改。\n按「確定」放棄修改並離開；按「取消」返回編輯。".Replace("\n",Environment.NewLine),Margin=new Padding(3,3,3,18)});
  var actions=new FlowLayoutPanel{AutoSize=true,WrapContents=false};layout.Controls.Add(actions);
  var discard=new Button{Text="確定",AutoSize=true,Padding=new Padding(12,6,12,6),DialogResult=DialogResult.Yes};
  var cancel=new Button{Text="取消",AutoSize=true,Padding=new Padding(12,6,12,6),DialogResult=DialogResult.Cancel};
  var save=new Button{Text="儲存並關閉",AutoSize=true,Padding=new Padding(12,6,12,6),DialogResult=DialogResult.Retry};
  actions.Controls.Add(discard);actions.Controls.Add(cancel);actions.Controls.Add(save);CancelButton=cancel;AcceptButton=cancel;
 }
}