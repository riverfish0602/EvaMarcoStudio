using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: System.Reflection.AssemblyTitle("FishMarco")]
[assembly: System.Reflection.AssemblyProduct("FishMarco")]
[assembly: System.Reflection.AssemblyDescription("生活俠的小幫手")]

public class Step {
 public string Notes {get;set;} public int Sequence {get;set;} public string Type {get;set;} public int X {get;set;} public int Y {get;set;}
 public string Value {get;set;} public int Hold {get;set;} public int Delay {get;set;}
}
public class Template {
 public int Version {get;set;} [ScriptIgnore] public int Repeats {get;set;} public int Gap {get;set;} public List<Step> Steps {get;set;}
 public Template(){Version=1;Repeats=0;Gap=1000;Steps=new List<Step>();}
}
static class Native {
 [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
 [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h,int id,uint mod,uint key);
 [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h,int id);
 [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint n,INPUT[] inputs,int size);
 [DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
 [StructLayout(LayoutKind.Sequential)] struct MOUSE {public int x,y;public uint data,flags,time;public UIntPtr extra;}
 [StructLayout(LayoutKind.Sequential)] struct KEY {public ushort vk,scan;public uint flags,time;public UIntPtr extra;}
 [StructLayout(LayoutKind.Explicit)] struct UNION {[FieldOffset(0)] public MOUSE mouse;[FieldOffset(0)] public KEY key;}
 [StructLayout(LayoutKind.Sequential)] struct INPUT {public uint type;public UNION u;}
 static void Send(INPUT i){if(SendInput(1,new[]{i},Marshal.SizeOf(typeof(INPUT)))!=1)throw new Exception("Windows 未接受輸入。請確認目標程式的權限層級。");}
 public static void Key(ushort k,bool up){uint f=up?2u:0u;if(k>=33&&k<=46)f|=1;Send(new INPUT{type=1,u=new UNION{key=new KEY{vk=k,flags=f}}});}
 public static void Mouse(string button,bool up){uint f=button=="右鍵"?8u:button=="中鍵"?32u:2u;Send(new INPUT{type=0,u=new UNION{mouse=new MOUSE{flags=up?f*2:f}}});}
 public static void Move(int x,int y){if(!SetCursorPos(x,y))throw new Exception("無法移動滑鼠。");}
}
public class PointMarker {
 public Point Position; public string Label; public bool Preview;
}
// 視窗樣式、不搶焦點、滑鼠穿透都由 OverlayForm 處理，這裡只留下自己的差異：
// 覆蓋指定螢幕、用 Magenta 當透明色、畫出座標標示。
public class MarkerOverlay:OverlayForm {
 public List<PointMarker> Markers=new List<PointMarker>();
 public MarkerOverlay(Rectangle bounds){Bounds=bounds;BackColor=Color.Magenta;TransparencyKey=Color.Magenta;}
 protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);PaintMarkers(e.Graphics,ClientRectangle,Location,Markers);}
 public static void PaintMarkers(Graphics g,Rectangle canvas,Point origin,IEnumerable<PointMarker> markers){
  g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
  using(var font=new Font("Microsoft JhengHei UI",11,FontStyle.Bold))foreach(var marker in markers){
   int px=marker.Position.X-origin.X,py=marker.Position.Y-origin.Y;if(!canvas.Contains(px,py))continue;
   Color color=marker.Preview?Color.FromArgb(213,91,12):Color.FromArgb(0,102,204);
   using(var outline=new Pen(Color.White,5))using(var pen=new Pen(color,2)){
    g.DrawLine(outline,px-13,py,px+13,py);g.DrawLine(outline,px,py-13,px,py+13);g.DrawEllipse(outline,px-7,py-7,14,14);
    g.DrawLine(pen,px-13,py,px+13,py);g.DrawLine(pen,px,py-13,px,py+13);g.DrawEllipse(pen,px-7,py-7,14,14);
   }
   SizeF size=g.MeasureString(marker.Label,font);int w=(int)size.Width+14,h=(int)size.Height+8;
   int bx=Math.Max(0,Math.Min(px+16,canvas.Width-w)),by=Math.Max(0,Math.Min(py-24,canvas.Height-h));
   using(var brush=new SolidBrush(color)){g.FillRectangle(brush,bx,by,w,h);}g.DrawString(marker.Label,font,Brushes.White,bx+7,by+4);
  }
 }
}
public class MainForm:Form {
 readonly ActionGrid grid=new ActionGrid(); readonly ComboBox type=new ComboBox(),button=new ComboBox();
 readonly NumericUpDown x=Num(-100000,100000,0), y=Num(-100000,100000,0),hold=Num(0,600000,50),delay=WaitUnits.Control(),repeat=Num(0,1000000,0),gap=WaitUnits.Control();
 readonly KeyPicker keys=new KeyPicker(); readonly Label status=new Label{AutoSize=true,Text="就緒｜拖曳十字定位 · F9 開始 · F10 停止"};
 readonly List<Control> editControls=new List<Control>();
  CancellationTokenSource cancel; bool capturing=false; bool hotkeysReady; bool screenReady; bool editingDialog;
 readonly FlowLayoutPanel mouseFields=new FlowLayoutPanel(),keyFields=new FlowLayoutPanel();
 readonly Button showMarkers=new Button{Text="關閉螢幕點位",AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,FlatStyle=FlatStyle.Flat,Padding=new Padding(8,5,8,5),Margin=new Padding(8,2,0,4),BackColor=Color.White,UseVisualStyleBackColor=false};bool markersVisible=true;bool syncingEditor;
 readonly List<MarkerOverlay> overlays=new List<MarkerOverlay>();
 readonly TextBox notes=new TextBox{Width=750,Height=38,Multiline=true,ScrollBars=ScrollBars.Vertical,MaxLength=int.MaxValue};bool rebuilding;string actionState="[]";Stack<string> undoActions=new Stack<string>();List<Step> copiedActions=new List<Step>();string templateName="未命名範本";string currentTemplatePath;string savedSnapshot; Control holdLabel; bool draftVisible; CrosshairDrag newDrag; SequenceForm sequenceWindow; ScanStudioForm scanWindow; SequencePlan savedSequence=new SequencePlan();
 static NumericUpDown Num(int min,int max,int value){return new NumericUpDown{Minimum=min,Maximum=max,Value=value,Width=100};}
 static bool selfTesting;SequenceItem editingSequenceItem;string sessionLoadError;
 public MainForm(){
 Icon=AppIdentity.Icon;DoubleBuffered=true;Text="生活俠的小幫手";Size=new Size(1160,940);MinimumSize=new Size(1160,700);BackColor=Color.FromArgb(245,247,250);Font=new Font("Microsoft JhengHei UI",10);StartPosition=FormStartPosition.CenterScreen;
 var shell=new Panel{Dock=DockStyle.Fill};Controls.Add(shell);var scroll=new Panel{Dock=DockStyle.Fill,AutoScroll=true};shell.Controls.Add(scroll);var footer=new TableLayoutPanel{Dock=DockStyle.Bottom,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,RowCount=2,ColumnCount=1,Padding=new Padding(16,4,16,4)};footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));for(int i=0;i<2;i++)footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));shell.Controls.Add(footer);
 var layout=new TableLayoutPanel{Dock=DockStyle.Fill,MinimumSize=new Size(0,540),AutoSize=false,AutoSizeMode=AutoSizeMode.GrowAndShrink,RowCount=3,ColumnCount=1,Padding=new Padding(16,12,16,4)};layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));scroll.Controls.Add(layout);
 for(int i=0;i<3;i++)layout.RowStyles.Add(new RowStyle(i==2?SizeType.Percent:SizeType.AutoSize,i==2?100:0));
 var toolbar=Row();layout.Controls.Add(Section(Color.FromArgb(170,170,170),toolbar),0,0);AddButton(toolbar,"新增範本",()=>{if(ConfirmReplace()){editingSequenceItem=null;grid.Rows.Clear();draftVisible=false;RefreshMarkers();repeat.Value=0;gap.Value=1;delay.Value=1;keys.Value="Space";notes.Text="";templateName="未命名範本";Text="生活俠的小幫手｜未命名";MarkSaved(null);}});AddButton(toolbar,"載入範本",LoadTemplate);AddButton(toolbar,"另存範本",SaveTemplate);AddButton(toolbar,"範本組合…",OpenSequence);AddButton(toolbar,"顏色掃描小工具",OpenScanStudio);

 showMarkers.Font=Font;showMarkers.FlatAppearance.BorderColor=Color.Silver;showMarkers.Click+=(s,e)=>{markersVisible=!markersVisible;showMarkers.Text=markersVisible?"關閉螢幕點位":"顯示螢幕點位";showMarkers.BackColor=markersVisible?Color.White:Color.FromArgb(235,235,235);RefreshMarkers();};
 var editor=new BufferedEditorPanel{Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,RowCount=4,ColumnCount=1,BackColor=Color.Transparent,Padding=new Padding(4)};editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
 for(int i=0;i<4;i++)editor.RowStyles.Add(new RowStyle(i==1?SizeType.Absolute:SizeType.AutoSize,i==1?58:0));layout.Controls.Add(Section(Color.FromArgb(238,238,238),editor),0,1);
 type.Width=200;type.MinimumSize=new Size(200,0);type.DropDownWidth=200;button.Width=160;type.DropDownStyle=button.DropDownStyle=ComboBoxStyle.DropDownList;type.Items.AddRange(new object[]{"滑鼠點擊","鍵盤按壓","等待"});type.SelectedIndex=0;button.Items.AddRange(new object[]{"左鍵","右鍵","中鍵"});button.SelectedIndex=0;
 var choice=Row();editor.Controls.Add(choice,0,0);Field(choice,"動作類型",type);choice.Controls.Add(new Label{Text="選擇類型 → 設定內容 → 加入動作",AutoSize=true,ForeColor=Color.DimGray,Margin=new Padding(20,9,0,0)});
 var details=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,FlowDirection=FlowDirection.TopDown,WrapContents=false,Margin=Padding.Empty,Padding=Padding.Empty};editor.Controls.Add(details,0,1);mouseFields.Dock=keyFields.Dock=DockStyle.None;mouseFields.AutoSize=keyFields.AutoSize=true;mouseFields.AutoSizeMode=keyFields.AutoSizeMode=AutoSizeMode.GrowAndShrink;mouseFields.Margin=keyFields.Margin=Padding.Empty;mouseFields.Padding=keyFields.Padding=new Padding(0,4,0,4);details.Controls.Add(mouseFields);details.Controls.Add(keyFields);
 x.Visible=false;y.Visible=false;Field(mouseFields,"按鈕",button);
 newDrag=new CrosshairDrag(()=>new Point((int)x.Value,(int)y.Value),p=>{x.Value=p.X;y.Value=p.Y;draftVisible=true;RefreshMarkers();});mouseFields.Controls.Add(newDrag);editControls.Add(newDrag);
 mouseFields.Controls.Add(new Label{Text="放開十字後，按「加入動作」",AutoSize=true,Margin=new Padding(8,12,0,0)});
 Field(keyFields,"單鍵／組合鍵",keys);
 var timing=Row();editor.Controls.Add(timing,0,2);Field(timing,"按住時間（ms）",hold);holdLabel=timing.Controls[0];Field(timing,"動作後等待（sec）",delay);
 var notesRow=Row();editor.Controls.Add(notesRow,0,3);Field(notesRow,"備註（選填）",notes);AddButton(timing,"＋ 加入動作",()=>{var a=EditorStep();ValidateStep(a);AddStep(a);draftVisible=false;RefreshMarkers();});AddButton(timing,"套用至所選動作",ApplyEditorToSelected);
 type.SelectedIndexChanged+=(s,e)=>{if(syncingEditor)return;UpdateFields();RefreshMarkers();};x.ValueChanged+=(s,e)=>{draftVisible=true;RefreshMarkers();};y.ValueChanged+=(s,e)=>{draftVisible=true;RefreshMarkers();};UpdateFields(); grid.Dock=DockStyle.Fill;grid.ReadOnly=false;grid.EditMode=DataGridViewEditMode.EditProgrammatically;grid.AllowUserToAddRows=false;grid.AllowUserToDeleteRows=false;grid.MultiSelect=true;grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;grid.AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill;grid.RowHeadersVisible=false;
 foreach(string col in new[]{"序號","動作","X","Y","按鍵／滑鼠","按住（ms）","動作後等待（sec）","備註"})grid.Columns.Add(col,col);foreach(DataGridViewColumn column in grid.Columns)column.SortMode=DataGridViewColumnSortMode.NotSortable;grid.Columns[0].FillWeight=40;grid.Columns[6].DefaultCellStyle.Format="0.0##";grid.BackgroundColor=Color.White;grid.BorderStyle=BorderStyle.None;grid.EnableHeadersVisualStyles=false;grid.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(227,235,246);grid.ColumnHeadersHeight=52;grid.RowTemplate.Height=40;grid.DefaultCellStyle.Padding=new Padding(5,3,5,3);layout.Controls.Add(grid,0,2);grid.SelectionChanged+=(s,e)=>{if(rebuilding||grid.SelectedRows.Count!=1)return;var a=(Step)grid.SelectedRows[0].Tag;if(a==null)return;syncingEditor=true;SuspendEditor(editor);try{type.SelectedItem=a.Type;x.Value=a.X;y.Value=a.Y;if(a.Type=="滑鼠點擊")button.SelectedItem=a.Value;else if(a.Type=="鍵盤按壓")keys.Value=a.Value;hold.Value=a.Hold;delay.Value=a.Delay/1000m;notes.Text=a.Notes??"";draftVisible=false;UpdateFields();}finally{ResumeEditor(editor);syncingEditor=false;}RefreshMarkers();};
 grid.ReorderRequested+=(rows,target)=>ReorderActions(rows,target);SetupContextMenu();
 var run=Row();run.WrapContents=false;run.Dock=DockStyle.Fill;var execution=new TableLayoutPanel{Dock=DockStyle.Top,Height=76,AutoSize=false,ColumnCount=2,RowCount=1,Margin=Padding.Empty};execution.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));execution.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));execution.RowStyles.Add(new RowStyle(SizeType.Percent,100));execution.Controls.Add(run,0,0);showMarkers.Anchor=AnchorStyles.Right|AnchorStyles.Top;execution.Controls.Add(showMarkers,1,0);footer.Controls.Add(Section(Color.FromArgb(238,238,238),execution),0,0);Field(run,"循環次數（0＝持續）",repeat);Field(run,"循環後等待 (sec)",gap);AddButton(run,"開始 F9（倒數 3 秒）",async()=>await Run());var startButton=(Button)run.Controls[run.Controls.Count-1];startButton.Font=new Font(Font.FontFamily,14,FontStyle.Bold);startButton.BackColor=Color.LightGreen;startButton.UseVisualStyleBackColor=false;var stop=new Button{Text="停止 F10",AutoSize=true,BackColor=Color.MistyRose,Font=new Font(Font.FontFamily,14,FontStyle.Bold)};stop.Padding=new Padding(8,3,8,3);stop.Margin=new Padding(0,2,10,4);stop.Click+=(s,e)=>Stop();run.Controls.Add(stop);status.Margin=new Padding(4,6,4,6);footer.Controls.Add(status,0,1);
 ResetActionHistory();MarkSaved(null);if(!selfTesting){try{savedSequence=SequenceSession.Load(SequenceSession.DefaultPath);}catch(Exception ex){sessionLoadError=ex.Message;}}Shown+=(s,e)=>{screenReady=true;RefreshMarkers();if(sessionLoadError!=null)MessageBox.Show(this,"無法恢復前次範本組合："+sessionLoadError,"恢復失敗",MessageBoxButtons.OK,MessageBoxIcon.Warning);};FormClosed+=(s,e)=>{ClearMarkers();CloseScanStudio();};FormClosing+=(s,e)=>{if(capturing){e.Cancel=true;return;}if(cancel!=null){e.Cancel=true;Stop();status.Text="正在停止並釋放按鍵，完成後可關閉視窗。";return;}if(!ConfirmLeave()){e.Cancel=true;return;}try{SaveSequenceSession();}catch(Exception ex){e.Cancel=true;MessageBox.Show(this,"無法保存範本組合："+ex.Message,"自動儲存失敗",MessageBoxButtons.OK,MessageBoxIcon.Warning);}};
 }
 // 顏色掃描小工具是非模態工具窗，可與動作編輯同時使用；再次按下只帶回既有視窗。
 // 視窗自己註冊 F7／F8 熱鍵，並在關閉時釋放，所以主視窗這邊不需要管熱鍵。
 void OpenScanStudio(){
  if(scanWindow!=null&&!scanWindow.IsDisposed){if(!scanWindow.Visible)scanWindow.Show(this);scanWindow.Activate();return;}
  scanWindow=new ScanStudioForm();scanWindow.FormClosed+=(s,e)=>{var closed=scanWindow;scanWindow=null;if(closed!=null)closed.Dispose();};scanWindow.Show(this);
 }
 void CloseScanStudio(){if(scanWindow==null||scanWindow.IsDisposed){scanWindow=null;return;}var window=scanWindow;scanWindow=null;window.Close();window.Dispose();}
 void OpenSequence(){
  if(cancel!=null||capturing||editingDialog)return;keys.StopCapture();editingDialog=true;RefreshMarkers();
  try{using(var window=new SequenceForm(()=>hotkeysReady,item=>EditSequenceTemplate(item))){sequenceWindow=window;foreach(var item in savedSequence.Items)window.AddItem(item);window.SetTransition(savedSequence.TransitionDelay);window.SetOuterLoop(savedSequence.OuterRuns,savedSequence.OuterGap);window.ShowDialog(this);savedSequence=window.Current();SaveSequenceSession();}}
  finally{sequenceWindow=null;editingDialog=false;RefreshMarkers();}
 }
 void SaveSequenceSession(){if(!selfTesting)SequenceSession.Save(SequenceSession.DefaultPath,savedSequence);}
 bool EditSequenceTemplate(SequenceItem item,Func<bool> confirm=null){
  var template=Json.Copy(item.Template);Validate(template);
  if(!(confirm??ConfirmLeave)())return false;
  ApplyTemplate(template);editingSequenceItem=item;templateName=item.Name;Text="生活俠的小幫手｜"+item.Name;
  string path=SequenceForm.MissingSource(item)?null:item.SourcePath;MarkSaved(path);
  if(path!=null){try{var source=Json.Read<Template>(File.ReadAllText(path));Validate(source);savedSnapshot=Json.Write(source);}catch{savedSnapshot=null;}}
  status.Text=path==null?"已開啟組合內的範本；儲存時請選擇檔案位置。":"已開啟範本："+path;return true;
 }
 void SetupCellEditing(){
  grid.CellDoubleClick+=(s,e)=>{if(e.RowIndex<0||e.ColumnIndex<0||cancel!=null||capturing||editingDialog)return;var step=grid.Rows[e.RowIndex].Tag as Step;if(step==null||!CellEdits.CanEdit(step,e.ColumnIndex))return;grid.CurrentCell=grid.Rows[e.RowIndex].Cells[e.ColumnIndex];grid.BeginEdit(true);};
  grid.CellBeginEdit+=(s,e)=>{var step=grid.Rows[e.RowIndex].Tag as Step;e.Cancel=cancel!=null||capturing||editingDialog||step==null||!CellEdits.CanEdit(step,e.ColumnIndex);};
  grid.CellValidating+=(s,e)=>{if(rebuilding||grid.Rows[e.RowIndex].Tag==null||!CellEdits.CanEdit((Step)grid.Rows[e.RowIndex].Tag,e.ColumnIndex))return;try{CellEdits.Apply((Step)grid.Rows[e.RowIndex].Tag,e.ColumnIndex,Convert.ToString(e.FormattedValue));grid.Rows[e.RowIndex].ErrorText="";}catch(Exception ex){e.Cancel=true;grid.Rows[e.RowIndex].ErrorText=ex.Message;status.Text=ex.Message+"（Esc 取消）";}};
  grid.CellEndEdit+=(s,e)=>{var row=grid.Rows[e.RowIndex];try{row.Tag=CellEdits.Apply((Step)row.Tag,e.ColumnIndex,Convert.ToString(row.Cells[e.ColumnIndex].Value));TrackActions();status.Text="已更新動作 #"+(row.Index+1)+" 的「"+grid.Columns[e.ColumnIndex].HeaderText+"」";}catch(Exception ex){status.Text=ex.Message+"，已保留原值。";}row.ErrorText="";grid.BeginInvoke(new Action(()=>{if(grid.IsDisposed||row.DataGridView!=grid)return;SetRow(row,(Step)row.Tag);if(!grid.IsCurrentCellInEditMode&&row.Selected&&grid.SelectedRows.Count==1){grid.ClearSelection();row.Selected=true;}}));};
  grid.DataError+=(s,e)=>{e.ThrowException=false;status.Text="欄位格式不正確，請重新輸入或按 Esc 取消。";};
 }
 void SetupContextMenu(){
  var menu=new ContextMenuStrip();menu.Items.Add("複製",null,(s,e)=>DuplicateSelected());menu.Items.Add("修改",null,(s,e)=>EditSelected());var delete=(ToolStripMenuItem)menu.Items.Add("刪除",null,(s,e)=>DeleteSelected());delete.ShortcutKeyDisplayString="Del";
  grid.MouseUp+=(s,e)=>{var hit=grid.HitTest(e.X,e.Y);if(e.Button!=MouseButtons.Right||hit.RowIndex<0||cancel!=null||capturing||editingDialog)return;if(!grid.Rows[hit.RowIndex].Selected){grid.ClearSelection();grid.Rows[hit.RowIndex].Selected=true;grid.CurrentCell=grid.Rows[hit.RowIndex].Cells[Math.Max(0,hit.ColumnIndex)];}menu.Show(Cursor.Position);};
  SetupCellEditing();Disposed+=(s,e)=>menu.Dispose();
 }
 public static Step CopyStep(Step a){return new Step{Sequence=a.Sequence,Type=a.Type,X=a.X,Y=a.Y,Value=a.Value,Hold=a.Hold,Delay=a.Delay,Notes=a.Notes};}
 void DuplicateSelected(){
  if(cancel!=null||capturing||editingDialog||grid.SelectedRows.Count==0)return;
  if(grid.IsCurrentCellInEditMode&&!grid.EndEdit())return;
  var rows=grid.SelectedRows.Cast<DataGridViewRow>().OrderBy(r=>r.Index).ToArray();
  int index=rows.Last().Index+1;var copies=rows.Select(r=>CopyStep((Step)r.Tag)).ToArray();
  rebuilding=true;try{grid.Rows.Insert(index,copies.Length);for(int i=0;i<copies.Length;i++)SetRow(grid.Rows[index+i],copies[i]);grid.CurrentCell=grid.Rows[index].Cells[0];grid.ClearSelection();for(int i=0;i<copies.Length;i++)grid.Rows[index+i].Selected=true;}finally{rebuilding=false;}
  Renumber();draftVisible=false;RefreshMarkers();status.Text="已複製 "+copies.Length+" 個動作，新增於第 "+(index+1)+" 筆";
 }
 void DeleteSelected(){
  if(cancel!=null||capturing||editingDialog||KeyPicker.AnyRecording||newDrag.Dragging||grid.SelectedRows.Count==0)return;
  if(grid.IsCurrentCellInEditMode&&!grid.EndEdit())return;
  var rows=grid.SelectedRows.Cast<DataGridViewRow>().OrderBy(r=>r.Index).ToArray();int index=rows[0].Index;
  rebuilding=true;try{foreach(var row in rows)grid.Rows.Remove(row);grid.ClearSelection();if(grid.Rows.Count>0){index=Math.Min(index,grid.Rows.Count-1);grid.CurrentCell=grid.Rows[index].Cells[0];grid.Rows[index].Selected=true;}}finally{rebuilding=false;}
  Renumber();draftVisible=false;RefreshMarkers();status.Text="已刪除 "+rows.Length+" 個動作";
 }
 void ApplyEditorToSelected(){
  if(cancel!=null||capturing||editingDialog)return;
  var rows=grid.SelectedRows.Cast<DataGridViewRow>().OrderBy(r=>r.Index).ToArray();if(rows.Length==0){status.Text="請先選取要修改的動作。";return;}
  if(grid.IsCurrentCellInEditMode&&!grid.EndEdit())return;
  var value=EditorStep();ValidateStep(value);
  rebuilding=true;try{foreach(var row in rows)SetRow(row,CopyStep(value));}finally{rebuilding=false;}
  Renumber();draftVisible=false;RefreshMarkers();status.Text="已套用至 "+rows.Length+" 個動作";
 }
 void EditBatch(){
  if(cancel!=null||capturing||editingDialog)return;if(grid.IsCurrentCellInEditMode&&!grid.EndEdit())return;
  var rows=grid.SelectedRows.Cast<DataGridViewRow>().OrderBy(r=>r.Index).ToArray();if(rows.Length<2){MessageBox.Show(this,"請先用 Shift 選取至少兩個相同類型的動作。");return;}var steps=rows.Select(r=>(Step)r.Tag).ToList();if(steps.Select(a=>a.Type).Distinct().Count()!=1){MessageBox.Show(this,"批次修改僅限相同動作類型，請重新選取。");return;}
  editingDialog=true;RefreshMarkers();try{using(var dialog=new BatchEditor(steps)){if(dialog.ShowDialog(this)==DialogResult.OK){rebuilding=true;try{for(int i=0;i<rows.Length;i++)SetRow(rows[i],dialog.Result[i]);}finally{rebuilding=false;}Renumber();draftVisible=false;status.Text="已批次修改 "+rows.Length+" 個動作";}}}finally{editingDialog=false;RefreshMarkers();}
 }
 void EditSelected(){
  if(cancel!=null||capturing||editingDialog||grid.SelectedRows.Count==0)return;
  if(grid.SelectedRows.Count>1){EditBatch();return;}var row=grid.SelectedRows[0];editingDialog=true;RefreshMarkers();
  try{using(var editor=new StepEditor(CopyStep((Step)row.Tag))){if(editor.ShowDialog(this)==DialogResult.OK){SetRow(row,editor.Result);grid.ClearSelection();row.Selected=true;Renumber();draftVisible=false;status.Text="已修改動作 #"+(row.Index+1);}}}
  finally{editingDialog=false;RefreshMarkers();}
 }
 FlowLayoutPanel Row(){return new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=true,Padding=new Padding(0,0,0,0),Margin=new Padding(0,0,0,0)};}
 Panel Section(Color color,Control content){var group=new Panel{Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Padding=new Padding(12,8,12,8),Margin=new Padding(0,0,0,8),BackColor=color,ForeColor=color.GetBrightness()<0.5f?Color.White:Color.Black};group.Controls.Add(content);return group;}
 void Field(FlowLayoutPanel p,string label,Control c){var field=new FlowLayoutPanel{AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=false,Padding=new Padding(0,2,0,6),Margin=new Padding(0,2,12,4)};field.Controls.Add(new Label{Text=label,AutoSize=true,Margin=new Padding(0,9,8,0)});c.Margin=new Padding(0,2,0,2);field.Controls.Add(c);p.Controls.Add(field);editControls.Add(c);}
 void AddButton(FlowLayoutPanel p,string text,Action a){var b=new Button{Text=text,ForeColor=Color.Black,BackColor=SystemColors.Control,AutoSize=true,Padding=new Padding(8,3,8,3),Margin=new Padding(0,2,10,4)};b.Click+=(s,e)=>{try{a();}catch(Exception ex){MessageBox.Show(this,ex.Message,"無法完成",MessageBoxButtons.OK,MessageBoxIcon.Warning);}};p.Controls.Add(b);editControls.Add(b);}
 protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);bool b=Native.RegisterHotKey(Handle,9,0x4000,120),c=Native.RegisterHotKey(Handle,10,0x4000,121);hotkeysReady=c;if(!b||!c)status.Text="部分熱鍵被其他程式占用。"+(c?"可用 F10 停止。":"F10 無法註冊，暫停執行功能；請關閉占用程式再重開。");}
 protected override void OnHandleDestroyed(EventArgs e){for(int n=9;n<=10;n++)Native.UnregisterHotKey(Handle,n);base.OnHandleDestroyed(e);}
 protected override void WndProc(ref Message m){if(m.Msg==0x312){int id=m.WParam.ToInt32();if(id==9&&cancel==null&&!capturing&&!editingDialog&&!KeyPicker.AnyRecording&&!newDrag.Dragging){var task=Run();}if(id==10)Stop();}base.WndProc(ref m);}
 void CapturePosition(){if(type.Text!="滑鼠點擊")return;var p=Cursor.Position;x.Value=p.X;y.Value=p.Y;draftVisible=true;RefreshMarkers();status.Text="已擷取位置："+p.X+", "+p.Y;}
 void Stop(){if(cancel!=null)cancel.Cancel();if(sequenceWindow!=null)sequenceWindow.Stop();}
 void SetEditing(bool enabled){foreach(var c in editControls)c.Enabled=enabled;grid.Enabled=enabled;RefreshMarkers();}
 Step EditorStep(){return new Step{Type=type.Text,X=type.Text=="滑鼠點擊"?(int)x.Value:0,Y=type.Text=="滑鼠點擊"?(int)y.Value:0,Value=type.Text=="滑鼠點擊"?button.Text:type.Text=="鍵盤按壓"?keys.Value.Trim():"",Hold=type.Text=="等待"?0:(int)hold.Value,Delay=WaitUnits.ToMilliseconds(delay.Value),Notes=notes.Text};}
 void AddStep(Step s){int i=grid.Rows.Add();SetRow(grid.Rows[i],s);grid.ClearSelection();grid.Rows[i].Selected=true;Renumber();}
 void SetRow(DataGridViewRow r,Step s){r.Tag=s;r.DefaultCellStyle.BackColor=Color.Empty;r.DefaultCellStyle.ForeColor=Color.Empty;r.Cells[1].Style.BackColor=WaitUnits.RowColor(s.Type);s.Sequence=r.Index+1;r.SetValues(s.Sequence,s.Type,s.Type=="滑鼠點擊"?(object)s.X:"—",s.Type=="滑鼠點擊"?(object)s.Y:"—",s.Type=="等待"?"—":s.Value,s.Type=="等待"?(object)"—":s.Hold,s.Delay/1000m,s.Notes??"");TrackActions();RefreshMarkers();}
 void ReorderActions(int[] indexes,int boundary){
  if(cancel!=null||capturing||editingDialog||indexes.Length==0)return;
  var original=grid.Rows.Cast<DataGridViewRow>().Select(r=>(Step)r.Tag).ToList();var selected=new HashSet<Step>(indexes.Select(i=>original[i]));var sorted=ActionGrid.Reorder(original,indexes,boundary);
  rebuilding=true;try{grid.Rows.Clear();foreach(var step in sorted){int i=grid.Rows.Add();SetRow(grid.Rows[i],step);}grid.ClearSelection();foreach(DataGridViewRow row in grid.Rows)row.Selected=selected.Contains((Step)row.Tag);var first=grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r=>r.Selected);if(first!=null)grid.FirstDisplayedScrollingRowIndex=first.Index;}
  finally{rebuilding=false;}Renumber();draftVisible=false;RefreshMarkers();status.Text="已移動 "+indexes.Length+" 個動作，序號已更新";
 }
 void MoveRow(int offset){var indexes=grid.SelectedRows.Cast<DataGridViewRow>().Select(r=>r.Index).OrderBy(i=>i).ToArray();if(indexes.Length==0)return;int target=offset<0?indexes[0]-1:indexes.Last()+2;if(target<0||target>grid.Rows.Count)return;ReorderActions(indexes,target);} Template Current(){if(grid.IsCurrentCellInEditMode&&!grid.EndEdit())throw new Exception("請先修正正在編輯的儲存格，或按 Esc 取消。");Renumber();return new Template{Repeats=(int)repeat.Value,Gap=WaitUnits.ToMilliseconds(gap.Value),Steps=grid.Rows.Cast<DataGridViewRow>().Select(r=>(Step)r.Tag).ToList()};}
 static void SuspendEditor(Control c){c.SuspendLayout();foreach(Control child in c.Controls)SuspendEditor(child);}
 static void ResumeEditor(Control c){foreach(Control child in c.Controls)ResumeEditor(child);c.ResumeLayout(true);}
 void UpdateFields(){var editor=mouseFields.Parent.Parent;if(!syncingEditor)SuspendEditor(editor);try{mouseFields.Visible=type.Text=="滑鼠點擊";keyFields.Visible=type.Text=="鍵盤按壓";mouseFields.Parent.Visible=true;hold.Parent.Visible=type.Text!="等待";hold.Visible=holdLabel.Visible=type.Text!="等待";}finally{if(!syncingEditor)ResumeEditor(editor);}}
 void Renumber(){foreach(DataGridViewRow row in grid.Rows){var step=row.Tag as Step;if(step!=null){step.Sequence=row.Index+1;row.Cells[0].Value=step.Sequence;}}TrackActions();RefreshMarkers();}
 public static List<PointMarker> BuildMarkers(IEnumerable<Step> steps){return steps.Where(a=>a!=null&&a.Type=="滑鼠點擊").GroupBy(a=>new Point(a.X,a.Y)).Select(g=>new PointMarker{Position=g.Key,Label=string.Join(", ",g.Select(a=>a.Sequence.ToString()))}).ToList();}
 void ClearMarkers(){foreach(var o in overlays)o.Dispose();overlays.Clear();}
 void RefreshMarkers(){
  if(!screenReady||rebuilding||syncingEditor)return;
  if(!markersVisible||cancel!=null||capturing||editingDialog){ClearMarkers();return;}
  var markers=BuildMarkers(grid.Rows.Cast<DataGridViewRow>().Select(r=>r.Tag as Step));
  if(draftVisible&&type.Text=="滑鼠點擊"){
   var position=new Point((int)x.Value,(int)y.Value);var existing=markers.FirstOrDefault(m=>m.Position==position);
   if(existing!=null){existing.Preview=true;existing.Label+=" · 預覽";}else markers.Add(new PointMarker{Position=position,Label="預覽",Preview=true});
  }
  if(markers.Count==0){ClearMarkers();return;}
  var screens=Screen.AllScreens;
  if(overlays.Count!=screens.Length||overlays.Where((o,i)=>o.Bounds!=screens[i].Bounds).Any()){ClearMarkers();foreach(var screen in screens)overlays.Add(new MarkerOverlay(screen.Bounds));}
  foreach(var overlay in overlays){overlay.Markers=markers;if(!overlay.Visible)overlay.Show();overlay.Invalidate();}
 }
 string Snapshot(){return Json.Write(Current());}
 void MarkSaved(string path){currentTemplatePath=path;savedSnapshot=Snapshot();}
 bool HasUnsavedChanges(){return savedSnapshot!=Snapshot();}
 bool ConfirmLeave(){
  try{if(!HasUnsavedChanges())return true;using(var dialog=new UnsavedChangesDialog()){return ResolveLeaveChoice(dialog.ShowDialog(sequenceWindow!=null?(IWin32Window)sequenceWindow:this));}}
  catch(Exception ex){MessageBox.Show(this,ex.Message,"請先完成或取消編輯");return false;}
 }
 bool ResolveLeaveChoice(DialogResult result){if(result==DialogResult.Yes)return true;if(result!=DialogResult.Retry)return false;SaveCurrentTemplate();return !HasUnsavedChanges();}
 bool ConfirmReplace(){return ConfirmLeave();}
 void SaveToPath(string path){
  var t=Current();Validate(t);string content=Json.Write(t);string fullPath=Path.GetFullPath(path);
  JsonFile.Write(fullPath,content);
  if(editingSequenceItem!=null){editingSequenceItem.Template=Json.Read<Template>(content);editingSequenceItem.SourcePath=fullPath;}currentTemplatePath=fullPath;savedSnapshot=content;templateName=Path.GetFileNameWithoutExtension(fullPath);Text="生活俠的小幫手｜"+Path.GetFileName(fullPath);status.Text="已儲存："+Path.GetFileName(fullPath);
 }
 void SaveTemplate(){using(var d=new SaveFileDialog{Filter="動作範本 (*.json)|*.json",DefaultExt="json",FileName=currentTemplatePath==null?"我的動作範本.json":Path.GetFileName(currentTemplatePath),InitialDirectory=currentTemplatePath==null?null:Path.GetDirectoryName(currentTemplatePath)})if(d.ShowDialog(sequenceWindow!=null?(IWin32Window)sequenceWindow:this)==DialogResult.OK)SaveToPath(d.FileName);}
 void SaveCurrentTemplate(){if(currentTemplatePath==null)SaveTemplate();else SaveToPath(currentTemplatePath);}
 protected override bool ProcessCmdKey(ref Message msg,Keys keyData){
  if(!KeyPicker.AnyRecording&&!capturing&&!editingDialog&&cancel==null&&!newDrag.Dragging&&!grid.IsCurrentCellInEditMode){if(keyData==(Keys.Control|Keys.Z)&&!(ActiveControl is TextBoxBase)&&!(ActiveControl is NumericUpDown)){UndoAction();return true;}if(grid.ContainsFocus){if(keyData==(Keys.Control|Keys.C)){CopyActions();return true;}if(keyData==(Keys.Control|Keys.V)){PasteActions();return true;}}}
  if(keyData==Keys.Delete&&grid.ContainsFocus&&!grid.IsCurrentCellInEditMode&&!KeyPicker.AnyRecording){DeleteSelected();return true;}
  if(keyData==(Keys.Control|Keys.S)&&!editingDialog&&!KeyPicker.AnyRecording){if(cancel==null&&!capturing&&!newDrag.Dragging){try{SaveCurrentTemplate();}catch(Exception ex){MessageBox.Show(this,"儲存失敗："+ex.Message,"無法儲存",MessageBoxButtons.OK,MessageBoxIcon.Error);}}return true;}return base.ProcessCmdKey(ref msg,keyData);
 }
 // 每次編輯都把整份動作清單序列化後存進復原堆疊。大範本的快照可達 MB 等級，
 // 所以要有上限，否則長時間編輯會無限累積。超過兩倍才整理一次，攤平成本。
 const int UndoLimit=120;
 void TrackActions(){
  if(rebuilding||grid.Rows.Cast<DataGridViewRow>().Any(r=>r.Tag==null))return;
  string now=Json.Write(grid.Rows.Cast<DataGridViewRow>().Select(r=>(Step)r.Tag).ToList());if(now==actionState)return;
  undoActions.Push(actionState);actionState=now;
  if(undoActions.Count>UndoLimit*2)undoActions=new Stack<string>(undoActions.Take(UndoLimit).Reverse());
 }
 void ResetActionHistory(){actionState=Json.Write(grid.Rows.Cast<DataGridViewRow>().Select(r=>(Step)r.Tag).ToList());undoActions.Clear();}
 void ReplaceActions(IEnumerable<Step> steps){rebuilding=true;grid.SuspendLayout();try{grid.Rows.Clear();foreach(var step in steps){int i=grid.Rows.Add();SetRow(grid.Rows[i],step);}grid.ClearSelection();}finally{grid.ResumeLayout();rebuilding=false;}draftVisible=false;Renumber();}
 void ApplyTemplate(Template t){SuspendLayout();try{ReplaceActions(t.Steps);gap.Value=t.Gap/1000m;ResetActionHistory();}finally{ResumeLayout(true);}RefreshMarkers();}
 void UndoAction(){if(cancel!=null||capturing||editingDialog||undoActions.Count==0)return;string previous=undoActions.Pop();actionState=previous;ReplaceActions(Json.Read<List<Step>>(previous));status.Text="已復原上一個編輯動作";}
 void CopyActions(){if(grid.SelectedRows.Count==0)return;copiedActions=grid.SelectedRows.Cast<DataGridViewRow>().OrderBy(r=>r.Index).Select(r=>CopyStep((Step)r.Tag)).ToList();}
 void PasteActions(){if(cancel!=null||capturing||editingDialog||copiedActions.Count==0)return;int index=grid.SelectedRows.Count==0?grid.Rows.Count:grid.SelectedRows.Cast<DataGridViewRow>().Max(r=>r.Index)+1;rebuilding=true;try{grid.Rows.Insert(index,copiedActions.Count);for(int i=0;i<copiedActions.Count;i++)SetRow(grid.Rows[index+i],CopyStep(copiedActions[i]));grid.CurrentCell=grid.Rows[index].Cells[0];grid.ClearSelection();for(int i=0;i<copiedActions.Count;i++)grid.Rows[index+i].Selected=true;}finally{rebuilding=false;}Renumber();status.Text="已貼上 "+copiedActions.Count+" 個動作";}
 void LoadTemplate(){using(var d=new OpenFileDialog{Filter="動作範本 (*.json)|*.json"})if(d.ShowDialog()==DialogResult.OK){var t=Json.Read<Template>(File.ReadAllText(d.FileName));Validate(t);if(!ConfirmReplace())return;editingSequenceItem=null;ApplyTemplate(t);templateName=Path.GetFileNameWithoutExtension(d.FileName);Text="生活俠的小幫手｜"+Path.GetFileName(d.FileName);draftVisible=false;Renumber();MarkSaved(Path.GetFullPath(d.FileName));status.Text="已載入範本（Ctrl+S 可直接儲存）";}}
 public static ushort[] ParseKeys(string text){var result=new List<ushort>();foreach(var part in (text??"").Split('+')){string p=part.Trim();Keys k;switch(p.ToUpperInvariant()){case "CTRL":case "CONTROL":k=Keys.ControlKey;break;case "ALT":k=Keys.Menu;break;case "SHIFT":k=Keys.ShiftKey;break;case "WIN":k=Keys.LWin;break;default:if(p.Length==1&&char.IsDigit(p[0]))k=(Keys)(48+p[0]-'0');else if(!Enum.TryParse<Keys>(p,true,out k))throw new Exception("不支援的按鍵："+p);break;}int v=(int)k;if(v<8||v>254||k==Keys.F9||k==Keys.F10)throw new Exception("按鍵無效或為保留熱鍵："+p);if(result.Contains((ushort)v))throw new Exception("組合鍵不可重複。");result.Add((ushort)v);}return result.ToArray();}
 public static void ValidateStep(Step s){if(s==null||!(new[]{"滑鼠點擊","鍵盤按壓","等待"}).Contains(s.Type))throw new Exception("範本包含無效動作。");if(s.Hold<0||s.Hold>600000||s.Delay<0||s.Delay>86400000||Math.Abs((long)s.X)>100000||Math.Abs((long)s.Y)>100000)throw new Exception("時間或座標超出允許範圍。");if(s.Type=="鍵盤按壓")ParseKeys(s.Value);if(s.Type=="滑鼠點擊"&&!(new[]{"左鍵","右鍵","中鍵"}).Contains(s.Value))throw new Exception("滑鼠按鈕無效。");}
 public static void Validate(Template t){if(t==null||t.Version!=1||t.Steps==null||t.Steps.Count>10000||t.Repeats<0||t.Repeats>1000000||t.Gap<0||t.Gap>86400000)throw new Exception("範本格式或循環設定無效。");foreach(var s in t.Steps)ValidateStep(s);}
 async Task Run(){if(cancel!=null||capturing||editingDialog||KeyPicker.AnyRecording||newDrag.Dragging)return;try{if(!hotkeysReady)throw new Exception("F10 停止熱鍵不可用，請關閉占用熱鍵的程式後重新啟動。");var t=Current();Validate(t);if(t.Steps.Count==0)throw new Exception("請先加入動作。");cancel=new CancellationTokenSource();var token=cancel.Token;SetEditing(false);await CountdownOverlay.Run(token,s=>status.Text=s);using(var badge=new RunBadge()){badge.SetProgress(templateName,t.Repeats==0?-1:t.Repeats);badge.Show();await MacroRunner.RunTemplate(t,t.Repeats,token,s=>status.Text=s+"｜F10 停止",remaining:n=>badge.SetProgress(templateName,n),upcoming:badge.SetUpcoming);}status.Text="執行完成";}catch(OperationCanceledException){status.Text="已停止，按鍵已釋放";}catch(Exception ex){status.Text="執行中止："+ex.Message;MessageBox.Show(this,ex.Message,"執行中止");}finally{if(cancel!=null){cancel.Dispose();cancel=null;}SetEditing(true);}}
 [STAThread] public static void Main(string[] args){if(args.Contains("--self-test")){selfTesting=true;try{SelfTest();}catch(Exception ex){Report(ex);Environment.ExitCode=1;}return;}Native.SetProcessDPIAware();Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new MainForm());}
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
 // 這是 winexe，沒有主控台，Console.WriteLine 會被吞掉，自我測試失敗時看不到原因。
 // 所以把例外同時寫到執行檔旁邊的檔案，並盡量也送到呼叫端的主控台（從 PowerShell 啟動時有機會看到）。
 static void Report(Exception error){
  string text=DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+Environment.NewLine+error.ToString();
  try{File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"self-test-error.txt"),text,System.Text.Encoding.UTF8);}catch{}
  try{Console.Error.WriteLine(text);}catch{}
 }
 static void SelfTest(){
  FeatureTests.Run();Check(ParseKeys("Ctrl+Shift+S").Length==3&&ParseKeys("1")[0]==49,"Key parsing");
  foreach(string key in new[]{"F10","Ctrl+Ctrl","invalid",""}){bool rejected=false;try{ParseKeys(key);}catch{rejected=true;}Check(rejected,"Invalid key accepted");}
  var legacy=Json.Read<Template>("{\"Version\":1,\"Repeats\":2,\"Gap\":400,\"Steps\":[{\"Type\":\"滑鼠點擊\",\"X\":-100,\"Y\":200,\"Value\":\"左鍵\",\"Hold\":30,\"Delay\":500}]}");Validate(legacy);
  using(var f=new MainForm()){
   f.AddStep(legacy.Steps[0]);f.AddStep(new Step{Type="鍵盤按壓",Value="Ctrl+C",Hold=50,Delay=500});f.AddStep(new Step{Type="滑鼠點擊",X=-100,Y=200,Value="右鍵",Hold=40,Delay=600});
   var markers=BuildMarkers(f.Current().Steps);Check(markers.Count==1&&markers[0].Label=="1, 3"&&markers[0].Position.X==-100,"Shared coordinates and numbering");
   f.MoveRow(-1);Check(f.Current().Steps[1].Type=="滑鼠點擊","Reordering");Check(BuildMarkers(f.Current().Steps)[0].Label=="1, 2","Marker numbering after reorder");
   f.grid.Rows.RemoveAt(0);f.Renumber();Check(f.Current().Steps[0].Sequence==1&&f.Current().Steps[1].Sequence==2,"Delete renumbering");Check(BuildMarkers(f.Current().Steps)[0].Label=="1","Marker numbering after delete");
   var copy=Json.Copy(f.Current());Validate(copy);Check(copy.Steps[0].Sequence==1&&copy.Steps[0].X==-100&&copy.Steps[0].Delay==600,"Template roundtrip");
   var panel=f.Controls[0];f.Controls.Remove(panel);panel.Size=f.ClientSize;panel.CreateControl();panel.PerformLayout();
   f.type.SelectedItem="鍵盤按壓";Check(f.keys.Visible&&!f.x.Visible&&!f.button.Visible&&f.hold.Visible,"Keyboard-only fields");SavePreview(panel,"preview-keyboard.png");
   f.type.SelectedItem="滑鼠點擊";Check(!f.x.Visible&&!f.y.Visible&&f.button.Visible&&f.newDrag.Visible&&!f.keys.Visible&&f.hold.Visible,"Mouse editor uses button and drag only");SavePreview(panel,"preview-mouse.png");
   f.type.SelectedItem="等待";Check(!f.x.Visible&&!f.keys.Visible&&!f.hold.Visible&&f.delay.Visible,"Wait-only fields");Check(f.EditorStep().Value==""&&f.EditorStep().Hold==0,"Hidden fields excluded");SavePreview(panel,"preview-wait.png");
   f.grid.Rows.Clear();f.Renumber();Check(BuildMarkers(f.Current().Steps).Count==0,"Clear removes markers");panel.Dispose();
  }
    using(var f=new MainForm()){
   f.AddStep(new Step{Type="滑鼠點擊",X=10,Y=20,Value="左鍵",Hold=80,Delay=900});f.DuplicateSelected();
   Check(f.Current().Steps.Count==2&&f.Current().Steps[1].Sequence==2,"Duplicate insertion");f.Current().Steps[1].X=88;Check(f.Current().Steps[0].X==10,"Copy independence");
   var original=f.Current().Steps[0];using(var editor=new StepEditor(original)){editor.SetPosition(new Point(-450,670));var changed=editor.BuildResult();Check(changed.X==-450&&changed.Y==670&&changed.Hold==80&&changed.Delay==900,"Edit coordinates and timing");Check(original.X==10&&original.Y==20,"Cancel does not mutate original");var panel=editor.Controls[0];editor.Controls.Remove(panel);panel.Size=editor.ClientSize;panel.CreateControl();SavePreview(panel,"preview-edit-mouse.png");panel.Dispose();}
   using(var editor=new StepEditor(new Step{Type="鍵盤按壓",Value="Ctrl+Shift+S",Hold=60,Delay=120})){Check(editor.BuildResult().Value=="Ctrl+Shift+S","Key menu roundtrip");var panel=editor.Controls[0];editor.Controls.Remove(panel);panel.Size=editor.ClientSize;panel.CreateControl();SavePreview(panel,"preview-edit-keyboard.png");panel.Dispose();}
  }
  using(var f=new MainForm()){
   f.AddStep(new Step{Type="等待",Delay=100,Notes="A"});f.AddStep(new Step{Type="等待",Delay=200,Notes="B"});f.AddStep(new Step{Type="等待",Delay=300,Notes="C"});
   f.grid.ClearSelection();f.grid.Rows[0].Selected=true;f.grid.Rows[2].Selected=true;f.DuplicateSelected();
   Check(string.Join(",",f.Current().Steps.Select(a=>a.Notes))=="A,B,C,A,C","Multi-copy order and insertion");
   Check(f.grid.SelectedRows.Count==2,"Copies stay selected");
   f.Current().Steps[3].Notes="copy";Check(f.Current().Steps[0].Notes=="A","Multi-copy independence");
   f.DeleteSelected();Check(string.Join(",",f.Current().Steps.Select(a=>a.Notes))=="A,B,C","Delete selected copies");
   f.grid.SelectAll();f.DeleteSelected();Check(f.grid.Rows.Count==0,"Delete all actions");f.DeleteSelected();
  }
  using(var f=new MainForm()){
   Check(f.repeat.Value==0&&f.gap.Value==1,"Startup repeat and seconds");
   f.AddStep(new Step{Type="等待",Delay=125,Notes="first"});f.AddStep(new Step{Type="等待",Delay=250,Notes="second"});
   f.UndoAction();Check(f.Current().Steps.Count==1&&f.Current().Steps[0].Notes=="first","Undo add");
   f.grid.SelectAll();f.CopyActions();f.PasteActions();Check(f.Current().Steps.Count==2&&!object.ReferenceEquals(f.Current().Steps[0],f.Current().Steps[1]),"Paste independent copies");
   f.UndoAction();Check(f.Current().Steps.Count==1,"Undo paste");
   f.grid.SelectAll();f.DeleteSelected();f.UndoAction();Check(f.Current().Steps.Count==1,"Undo delete");
   f.SetRow(f.grid.Rows[0],new Step{Type="等待",Delay=999});f.UndoAction();Check(f.Current().Steps[0].Delay==125,"Undo edit");
   f.repeat.Value=7;Check(!Json.Write(f.Current()).Contains("Repeats"),"Repeat excluded from saved templates");
   f.ApplyTemplate(new Template{Repeats=99,Gap=1500,Steps=new List<Step>{new Step{Type="等待",Delay=1}}});Check(f.repeat.Value==7&&f.gap.Value==1.5m&&f.undoActions.Count==0,"Load preserves runtime repeat and converts legacy gap");
  }
  var sequenceItem=new SequenceItem{Name="test",Runs=1,Template=new Template{Steps=new List<Step>{new Step{Type="等待",Delay=1}}}};
  var editedItem=SequenceForm.EditCell(sequenceItem,4,"1.25");Check(editedItem.Template.Gap==1250&&sequenceItem.Template.Gap==1000,"Sequence gap seconds and copy isolation");
  Check(SequenceForm.EditCell(sequenceItem,2,"12").Runs==12&&SequenceForm.EditCell(sequenceItem,1,"renamed").Name=="renamed","Sequence inline fields");
  bool invalidRuns=false;try{SequenceForm.EditCell(sequenceItem,2,"0");}catch{invalidRuns=true;}Check(invalidRuns,"Sequence rejects endless stage");
  using(var f=new MainForm()){
   f.AddStep(new Step{Type="等待",Delay=100,Notes="A"});f.AddStep(new Step{Type="等待",Delay=200,Notes="B"});
   f.grid.SelectAll();f.type.SelectedItem="等待";f.delay.Value=1.5m;f.notes.Text="batch";f.ApplyEditorToSelected();
   Check(f.Current().Steps.All(a=>a.Delay==1500&&a.Notes=="batch")&&!object.ReferenceEquals(f.Current().Steps[0],f.Current().Steps[1]),"Top apply updates all selected independent actions");
   f.UndoAction();Check(f.Current().Steps[0].Notes=="A"&&f.Current().Steps[1].Delay==200,"Top batch apply undone in one operation");
   var panel=f.Controls[0];f.Controls.Remove(panel);panel.Size=f.ClientSize;panel.CreateControl();int top=-1;
   foreach(string kind in new[]{"滑鼠點擊","鍵盤按壓","等待"}){f.type.SelectedItem=kind;panel.PerformLayout();if(top<0)top=f.grid.Top;Check(f.grid.Top==top,"Stable action editor height");}
   Check(f.showMarkers.Text=="關閉螢幕點位","Marker button action text");f.showMarkers.PerformClick();Check(!f.markersVisible&&f.showMarkers.Text=="顯示螢幕點位","Marker toggle");
   panel.Dispose();
  }
  using(var seconds=WaitUnits.Control()){Check(seconds.Text=="1.0","Default seconds show one decimal");seconds.Value=.125m;Check(seconds.Text=="0.125"&&WaitUnits.ToMilliseconds(seconds.Value)==125,"Seconds preserve sub-tenth precision");}
  using(var f=new MainForm()){
   string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"save-tests-close-"+Guid.NewGuid().ToString("N")+".json");f.AddStep(new Step{Type="等待",Delay=1});f.SaveToPath(path);f.AddStep(new Step{Type="等待",Delay=2});
   Check(!f.ResolveLeaveChoice(DialogResult.Cancel)&&f.HasUnsavedChanges(),"Close cancel preserves changes");
   Check(f.ResolveLeaveChoice(DialogResult.Retry)&&!f.HasUnsavedChanges()&&Json.Read<Template>(File.ReadAllText(path)).Steps.Count==2,"Save and close updates existing template");
   f.AddStep(new Step{Type="等待",Delay=3});Check(f.ResolveLeaveChoice(DialogResult.Yes)&&f.HasUnsavedChanges(),"Confirm leaves without saving");
  }
  using(var dialog=new UnsavedChangesDialog()){var panel=dialog.Controls[0];dialog.Controls.Remove(panel);panel.CreateControl();panel.PerformLayout();SavePreview(panel,"preview-unsaved.png");panel.Dispose();}
  {
   string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"save-tests-session-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);string session=Path.Combine(folder,"session.json"),source=Path.Combine(folder,"template.json");
   var item=new SequenceItem{Name="恢復範本",SourcePath=source,Runs=3,TransitionDelay=1250,Template=new Template{Gap=250,Steps=new List<Step>{new Step{Type="等待",Delay=100,Notes="persist"}}}};
   var plan=new SequencePlan{Items=new List<SequenceItem>{item}};SequenceSession.Save(session,plan);var restored=SequenceSession.Load(session);
   Check(restored.Items[0].SourcePath==source&&restored.Items[0].TransitionDelay==1250&&restored.Items[0].Template.Steps[0].Notes=="persist","Session retains source, timing, and embedded actions");
   Check(SequenceForm.MissingSource(restored.Items[0]),"Missing source detected");
   File.WriteAllText(source,Json.Write(item.Template));Check(!SequenceForm.MissingSource(restored.Items[0]),"Existing source accepted");
   plan.Items[0].Runs=7;SequenceSession.Save(session,plan);Check(SequenceSession.Load(session).Items[0].Runs==7,"Session overwrite restores latest state");
   using(var f=new MainForm()){
    f.AddStep(new Step{Type="等待",Delay=999});bool checkedBeforeSwitch=false;
    Check(!f.EditSequenceTemplate(item,()=>{checkedBeforeSwitch=true;return false;})&&checkedBeforeSwitch&&f.Current().Steps[0].Delay==999,"Cancel template switch preserves current actions");
    Check(f.EditSequenceTemplate(item,()=>true)&&f.Current().Steps[0].Delay==100&&!object.ReferenceEquals(f.Current().Steps[0],item.Template.Steps[0]),"Template switch loads independent embedded actions");
    f.SetRow(f.grid.Rows[0],new Step{Type="等待",Delay=333});f.SaveToPath(source);
    Check(item.Template.Steps[0].Delay==333&&Json.Read<Template>(File.ReadAllText(source)).Steps[0].Delay==333,"Saving edited sequence template updates source and combination");
   }
   using(var sequence=new SequenceForm(()=>true)){var missing=new SequenceItem{Name="遺失範本",SourcePath=Path.Combine(folder,"missing.json"),Runs=1,Template=item.Template};sequence.AddItem(missing);var actionGrid=(ActionGrid)typeof(SequenceForm).GetField("grid",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(sequence);Check(actionGrid.RowHeadersVisible&&actionGrid.Rows[0].ErrorText.Contains("找不到"),"Missing source row warning");}
  }
  legacy.Gap=-1;bool bad=false;try{Validate(legacy);}catch{bad=true;}Check(bad,"Invalid timing accepted");
  using(var bitmap=new Bitmap(760,320))using(var g=Graphics.FromImage(bitmap)){
   g.Clear(Color.FromArgb(245,247,250));MarkerOverlay.PaintMarkers(g,new Rectangle(0,0,760,320),new Point(-500,0),new[]{new PointMarker{Position=new Point(-400,100),Label="1"},new PointMarker{Position=new Point(-180,190),Label="3, 5"},new PointMarker{Position=new Point(30,100),Label="預覽",Preview=true},new PointMarker{Position=new Point(259,319),Label="7"}});bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"preview-markers.png"));
  }
  File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"self-test-result.txt"),"PASS: key parsing, legacy template import, sequence persistence, shared coordinate markers, negative coordinates, reorder/delete/clear numbering, mouse/keyboard/wait field visibility, hidden field normalization, timing validation, independent action duplication, edit coordinates, cancel isolation, key menu roundtrip. No input was sent.");
 }
 static void SavePreview(Control panel,string file){panel.PerformLayout();using(var bitmap=new Bitmap(panel.Width,panel.Height)){panel.DrawToBitmap(bitmap,new Rectangle(Point.Empty,panel.Size));bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,file));}}
}