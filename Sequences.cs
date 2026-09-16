using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

public class SequenceItem {public string SourcePath{get;set;} public int? TransitionDelay{get;set;} public string Name{get;set;} public int Runs{get;set;} public Template Template{get;set;}}
public class SequencePlan {public int OuterRuns{get;set;} public int OuterGap{get;set;}
 public string Kind{get;set;} public int Version{get;set;} public int TransitionDelay{get;set;} public List<SequenceItem> Items{get;set;}
 public SequencePlan(){OuterRuns=1;OuterGap=1000;Kind="MacroSequence";Version=1;TransitionDelay=1000;Items=new List<SequenceItem>();}
 public static void Validate(SequencePlan p){if(p==null||p.Kind!="MacroSequence"||p.Version!=1||p.Items==null||p.Items.Count>1000||p.OuterRuns<0||p.OuterRuns>1000000||p.OuterGap<0||p.OuterGap>86400000||p.TransitionDelay<0||p.TransitionDelay>86400000)throw new Exception("範本組合格式無效。");foreach(var i in p.Items){if(i==null||string.IsNullOrWhiteSpace(i.Name)||i.Runs<1||i.Runs>1000000||(i.TransitionDelay.HasValue&&(i.TransitionDelay<0||i.TransitionDelay>86400000)))throw new Exception("每個範本的執行次數須為 1～1000000。");MainForm.Validate(i.Template);if(i.Template.Steps.Count==0)throw new Exception(i.Name+" 沒有動作。");}}
}
public static class MacroRunner {
 public static async Task RunTemplate(Template t,int runs,CancellationToken token,Action<string> report,Func<Step,CancellationToken,Task> perform=null,Func<int,CancellationToken,Task> wait=null,Action<long> remaining=null,Action<Step,long> upcoming=null,Step following=null,int transitionDelay=0){
  MainForm.Validate(t);if(runs<0||runs>1000000||t.Steps.Count==0)throw new Exception("循環設定無效或範本沒有動作。");perform=perform??Perform;wait=wait??((ms,ct)=>Task.Delay(ms,ct));
  for(long cycle=1;runs==0||cycle<=runs;cycle++){if(remaining!=null)remaining(runs==0?-1:runs-cycle+1);
   for(int index=0;index<t.Steps.Count;index++){token.ThrowIfCancellationRequested();report("循環 "+cycle+(runs==0?"":" / "+runs)+" · 動作 "+(index+1)+" / "+t.Steps.Count);var current=t.Steps[index];bool repeat=runs==0||cycle<runs;var next=index+1<t.Steps.Count?t.Steps[index+1]:(repeat?t.Steps[0]:following);long extra=index+1<t.Steps.Count?0:(repeat?t.Gap+1L:transitionDelay+1L);if(upcoming!=null)upcoming(next,(current.Type=="等待"?0:current.Hold)+current.Delay+extra);await perform(current,token);if(upcoming!=null)upcoming(next,current.Delay+extra);await wait(current.Delay,token);}
   if(remaining!=null)remaining(runs==0?-1:runs-cycle);if(runs==0||cycle<runs)await wait(t.Gap,token);await wait(1,token);
  }
 }
 public static async Task RunSequence(SequencePlan plan,CancellationToken token,Action<string> report,Func<Step,CancellationToken,Task> perform=null,Func<int,CancellationToken,Task> wait=null,Action<string,long> progress=null,Action<Step,long> upcoming=null){
  SequencePlan.Validate(plan);if(plan.Items.Count==0)throw new Exception("請加入範本。");wait=wait??((ms,ct)=>Task.Delay(ms,ct));
  for(long cycle=1;plan.OuterRuns==0||cycle<=plan.OuterRuns;cycle++){
   bool another=plan.OuterRuns==0||cycle<plan.OuterRuns;
   for(int i=0;i<plan.Items.Count;i++){
    token.ThrowIfCancellationRequested();var item=plan.Items[i];bool nextItem=i<plan.Items.Count-1;
    int transition=nextItem?(item.TransitionDelay??plan.TransitionDelay):plan.OuterGap;
    Step next=nextItem?plan.Items[i+1].Template.Steps[0]:(another?plan.Items[0].Template.Steps[0]:null);
    string prefix="組合循環 "+cycle+" · 範本 "+(i+1)+" / "+plan.Items.Count+" · "+item.Name+"｜";
    await RunTemplate(item.Template,item.Runs,token,s=>report(prefix+s),perform,wait,n=>{if(progress!=null)progress(item.Name,n);},upcoming,next,transition);
    if(nextItem){if(upcoming!=null)upcoming(next,transition);await wait(transition,token);}
   }
   if(another){token.ThrowIfCancellationRequested();if(upcoming!=null)upcoming(plan.Items[0].Template.Steps[0],plan.OuterGap);await wait(plan.OuterGap,token);}
  }
 }
 static async Task Perform(Step s,CancellationToken token){
  if(s.Type=="滑鼠點擊"){
   if(!Screen.AllScreens.Any(sc=>sc.Bounds.Contains(s.X,s.Y)))throw new Exception("滑鼠座標不在目前螢幕範圍內。");Native.Move(s.X,s.Y);bool down=false;
   try{Native.Mouse(s.Value,false);down=true;await Task.Delay(s.Hold,token);}finally{if(down)Native.Mouse(s.Value,true);}
  }else if(s.Type=="鍵盤按壓"){
   var pressed=new List<ushort>();try{foreach(var k in MainForm.ParseKeys(s.Value)){Native.Key(k,false);pressed.Add(k);}await Task.Delay(s.Hold,token);}
   finally{Exception error=null;pressed.Reverse();foreach(var k in pressed){try{Native.Key(k,true);}catch(Exception ex){error=ex;}}if(error!=null)throw error;}
  }
 }
}
public class SequenceForm:Form {
 readonly ActionGrid grid=new ActionGrid{Dock=DockStyle.Fill,ReadOnly=false,EditMode=DataGridViewEditMode.EditProgrammatically,AllowUserToAddRows=false,AllowUserToDeleteRows=false,MultiSelect=true,SelectionMode=DataGridViewSelectionMode.FullRowSelect,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,RowHeadersVisible=true,RowHeadersWidth=32,BackgroundColor=Color.White};
 readonly NumericUpDown runs=new NumericUpDown{Minimum=1,Maximum=1000000,Value=1000,Width=120},gap=WaitUnits.Control(),cycleGap=WaitUnits.Control();
 readonly Label status=new Label{AutoSize=true,Text=""};readonly NumericUpDown outerRuns=new NumericUpDown{Minimum=0,Maximum=1000000,Value=1,Width=100},outerGap=WaitUnits.Control();
 readonly List<Control> editing=new List<Control>();readonly Func<bool> canRun;readonly Func<SequenceItem,bool> editTemplate;
 readonly Label estimate=new Label{Dock=DockStyle.Top,AutoSize=true,BackColor=Color.FromArgb(225,240,255),ForeColor=Color.Black,Padding=new Padding(12,10,12,10),Margin=new Padding(0,6,0,6)};
 int defaultTransition=1000;bool syncingFields;bool rebuilding;List<SequenceItem> copiedItems=new List<SequenceItem>();
 CancellationTokenSource cancellation;
 public SequenceForm(Func<bool> ready,Func<SequenceItem,bool> edit=null){editTemplate=edit;
  Icon=AppIdentity.Icon;canRun=ready;Text="範本組合";Size=new Size(1240,760);MinimumSize=new Size(1180,640);StartPosition=FormStartPosition.CenterParent;Font=new Font("Microsoft JhengHei UI",10);
  var layout=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=6,ColumnCount=1,Padding=new Padding(16)};Controls.Add(layout);
  layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));for(int i=0;i<6;i++)layout.RowStyles.Add(new RowStyle(i==3?SizeType.Percent:SizeType.AutoSize,i==3?100:0));
  var toolbar=Row();layout.Controls.Add(toolbar);Button(toolbar,"加入範本…",AddTemplates);Button(toolbar,"載入組合…",LoadPlan);Button(toolbar,"儲存組合…",SavePlan);UpdateEstimate();
  var batch=Row();batch.WrapContents=false;batch.BackColor=Color.FromArgb(238,238,238);batch.Padding=new Padding(10,8,10,8);AddField(batch,"範本循環次數",runs);AddField(batch,"循環後等待 (sec)",cycleGap);AddField(batch,"範本切換等待 (sec)",gap);layout.Controls.Add(batch);editing.Add(cycleGap);runs.ValueChanged+=(s,e)=>ApplyField(2);cycleGap.ValueChanged+=(s,e)=>ApplyField(4);gap.ValueChanged+=(s,e)=>ApplyField(5);
  layout.Controls.Add(new Label{AutoSize=true,Dock=DockStyle.Top,Padding=new Padding(0,8,0,12),Text="依序執行每列的循環次數，例如 A × 1000 → B × 1000 → C × 500。\nCtrl／Shift 多選；拖曳調整順序；雙擊欄位直接修改。上方數值會隨選取更新，變更後立即套用至所有所選範本。"});
  foreach(string name in new[]{"順序","範本","循環次數","動作數","循環後等待 (sec)","範本切換等待 (sec)"})grid.Columns.Add(name,name);grid.Columns[3].Visible=false;grid.Columns[0].FillWeight=35;grid.Columns[1].FillWeight=200;grid.RowTemplate.Height=38;grid.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.AutoSize;foreach(DataGridViewColumn c in grid.Columns){c.SortMode=DataGridViewColumnSortMode.NotSortable;c.MinimumWidth=70;}grid.Columns[4].MinimumWidth=175;grid.Columns[5].MinimumWidth=180;grid.Columns[5].DefaultCellStyle.Format="0.0##";grid.Columns[4].DefaultCellStyle.Format="0.0##";layout.Controls.Add(grid);SetupEditing();grid.ReorderRequested+=ReorderItems;grid.SelectionChanged+=(s,e)=>SyncFields();layout.Controls.Add(estimate);
  var footer=Row();layout.Controls.Add(footer);AddField(footer,"循環次數（0＝持續）",outerRuns);AddField(footer,"循環後等待 (sec)",outerGap);outerRuns.ValueChanged+=(s,e)=>UpdateEstimate();outerGap.ValueChanged+=(s,e)=>UpdateEstimate();editing.Add(outerRuns);editing.Add(outerGap);Button(footer,"執行組合",async()=>await Run());var start=(Button)footer.Controls[footer.Controls.Count-1];start.Font=new Font(Font.FontFamily,14,FontStyle.Bold);start.BackColor=Color.LightGreen;start.UseVisualStyleBackColor=false;var stop=new Button{Text="停止 F10",AutoSize=true,Font=new Font(Font.FontFamily,14,FontStyle.Bold)};stop.Click+=(s,e)=>Stop();footer.Controls.Add(stop);editing.Add(grid);editing.Add(runs);editing.Add(gap);
  Shown+=(s,e)=>WarnMissingSources();FormClosing+=(s,e)=>{if(cancellation!=null){e.Cancel=true;Stop();status.Text="正在停止，完成後可關閉。";return;}if(grid.IsCurrentCellInEditMode&&!grid.EndEdit())e.Cancel=true;};
 }
 static FlowLayoutPanel Row(){return new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=true,Padding=new Padding(0,4,0,4)};}
 void Button(FlowLayoutPanel row,string text,Action action){var b=new Button{Text=text,AutoSize=true};b.Click+=(s,e)=>{try{action();}catch(Exception ex){MessageBox.Show(this,ex.Message,"範本組合");}};row.Controls.Add(b);editing.Add(b);}
 public static decimal EstimateMilliseconds(SequencePlan plan){
  if(plan.Items.Count==0)return 0;if(plan.OuterRuns==0)return -1;
  decimal total=3000;for(int i=0;i<plan.Items.Count-1;i++)total+=plan.Items[i].TransitionDelay??plan.TransitionDelay;
  foreach(var item in plan.Items){
   decimal cycle=item.Template.Steps.Sum(step=>(decimal)step.Delay+(step.Type=="等待"?0:step.Hold));
   total+=cycle*item.Runs+(decimal)item.Template.Gap*(item.Runs-1)+item.Runs;
  }
  return 3000+(total-3000)*plan.OuterRuns+(decimal)plan.OuterGap*(plan.OuterRuns-1);
 }
 public static string FormatDuration(decimal milliseconds){
  decimal seconds=Math.Ceiling(milliseconds/1000m),days=Math.Floor(seconds/86400m);seconds%=86400m;
  decimal hours=Math.Floor(seconds/3600m);seconds%=3600m;decimal minutes=Math.Floor(seconds/60m);seconds%=60m;
  return (days>0?days+" 天 ":"")+(hours>0?hours+" 小時 ":"")+(minutes>0?minutes+" 分 ":"")+seconds+" 秒";
 }
 void UpdateEstimate(){
  var items=grid.Rows.Cast<DataGridViewRow>().Select(r=>r.Tag as SequenceItem).Where(i=>i!=null).ToList();
  var plan=new SequencePlan{OuterRuns=(int)outerRuns.Value,OuterGap=WaitUnits.ToMilliseconds(outerGap.Value),TransitionDelay=defaultTransition,Items=items};
  estimate.Text=items.Count==0?"預計總執行時間：0 秒":plan.OuterRuns==0?"預計總執行時間：持續循環（按 F10 停止）":"預計總執行時間："+FormatDuration(EstimateMilliseconds(plan))+"（含開始前 3 秒倒數）";
 }
 public static SequenceItem EditCell(SequenceItem source,int column,string value){
  var item=new SequenceItem{Name=source.Name,SourcePath=source.SourcePath,Runs=source.Runs,TransitionDelay=source.TransitionDelay,Template=new Template{Gap=source.Template.Gap,Steps=source.Template.Steps.Select(MainForm.CopyStep).ToList()}};
  if(column==1){if(string.IsNullOrWhiteSpace(value))throw new Exception("範本名稱不可空白。");item.Name=value.Trim();}
  else if(column==2){int count;if(!int.TryParse(value,out count)||count<1||count>1000000)throw new Exception("循環次數須為 1～1000000。");item.Runs=count;}
  else if(column==4)item.Template.Gap=WaitUnits.Parse(value);else if(column==5)item.TransitionDelay=WaitUnits.Parse(value);
  else throw new Exception("此欄位由範本內容自動計算。");return item;
 }
 void SetupEditing(){
  foreach(DataGridViewColumn column in grid.Columns)column.ReadOnly=column.Index!=1&&column.Index!=2&&column.Index!=4&&column.Index!=5;
  grid.CellEditRequested+=(row,col)=>{if(row<0||col<0)return;var cell=grid.Rows[row].Cells[col];if(cell.ReadOnly)return;grid.BeginInvoke(new Action(()=>BeginCellEdit(cell)));};
  grid.CellBeginEdit+=(s,e)=>e.Cancel=cancellation!=null||!(e.ColumnIndex==1||e.ColumnIndex==2||e.ColumnIndex==4||e.ColumnIndex==5);
  grid.CellValidating+=(s,e)=>{if(!grid.IsCurrentCellInEditMode)return;try{EditCell((SequenceItem)grid.Rows[e.RowIndex].Tag,e.ColumnIndex,Convert.ToString(e.FormattedValue));grid.Rows[e.RowIndex].ErrorText="";}catch(Exception ex){e.Cancel=true;grid.Rows[e.RowIndex].ErrorText=ex.Message;status.Text=ex.Message+"（Esc 取消）";}};
  grid.CellEndEdit+=(s,e)=>{var row=grid.Rows[e.RowIndex];try{row.Tag=EditCell((SequenceItem)row.Tag,e.ColumnIndex,Convert.ToString(row.Cells[e.ColumnIndex].Value));}catch(Exception ex){status.Text=ex.Message;}row.ErrorText="";Render(row);SyncFields();};
  grid.DataError+=(s,e)=>{e.ThrowException=false;status.Text="請輸入有效的欄位內容。";};
  var menu=new ContextMenuStrip();menu.Items.Add("複製",null,(s,e)=>DuplicateItem());menu.Items.Add("修改範本",null,(s,e)=>EditTemplate());menu.Items.Add("刪除",null,(s,e)=>DeleteItem());
  grid.MouseUp+=(s,e)=>{var hit=grid.HitTest(e.X,e.Y);if(e.Button!=MouseButtons.Right||hit.RowIndex<0||cancellation!=null)return;if(!grid.Rows[hit.RowIndex].Selected){grid.CurrentCell=grid.Rows[hit.RowIndex].Cells[Math.Max(0,hit.ColumnIndex)];grid.ClearSelection();grid.Rows[hit.RowIndex].Selected=true;}menu.Show(Cursor.Position);};Disposed+=(s,e)=>menu.Dispose();
 }
 public static bool MissingSource(SequenceItem item){return !string.IsNullOrWhiteSpace(item.SourcePath)&&!File.Exists(item.SourcePath);}
 void WarnMissingSources(){Renumber();var missing=grid.Rows.Cast<DataGridViewRow>().Select(r=>(SequenceItem)r.Tag).Where(MissingSource).ToArray();if(missing.Length>0)MessageBox.Show(this,"找不到 "+missing.Length+" 個範本來源檔案，已在該列標示驚嘆號。\n"+string.Join("\n",missing.Take(8).Select(i=>i.Name+"："+i.SourcePath)),"範本來源遺失",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
 void EditTemplate(){
  if(cancellation!=null||editTemplate==null)return;if(grid.IsCurrentCellInEditMode&&!grid.EndEdit())return;
  var selected=SelectedItems();if(selected.Length!=1){status.Text="請選取一個範本再修改。";return;}
  try{if(editTemplate((SequenceItem)selected[0].Tag)){DialogResult=DialogResult.OK;Close();}}catch(Exception ex){MessageBox.Show(this,ex.Message,"無法修改範本",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
 }
 void BeginCellEdit(DataGridViewCell cell){if(IsDisposed||grid.IsDisposed||cancellation!=null||cell.DataGridView!=grid||cell.ReadOnly)return;if(grid.IsCurrentCellInEditMode&&!grid.EndEdit())return;grid.Focus();grid.CurrentCell=cell;grid.BeginEdit(true);}
 DataGridViewRow[] SelectedItems(){return grid.SelectedRows.Cast<DataGridViewRow>().OrderBy(r=>r.Index).ToArray();}
 SequenceItem CloneItem(SequenceItem item){return Json.Copy(item);}
 static void AddField(FlowLayoutPanel row,string text,Control input){row.Controls.Add(new Label{Text=text,AutoSize=true,Margin=new Padding(8,7,6,3)});row.Controls.Add(input);}
 void SyncFields(){if(rebuilding)return;var row=SelectedItems().FirstOrDefault();if(row==null||row.Tag==null)return;var item=(SequenceItem)row.Tag;syncingFields=true;try{runs.Value=item.Runs;cycleGap.Value=item.Template.Gap/1000m;gap.Value=(item.TransitionDelay??defaultTransition)/1000m;}finally{syncingFields=false;}}
 void ApplyField(int column){
  if(syncingFields||rebuilding||cancellation!=null)return;if(grid.IsCurrentCellInEditMode&&!grid.EndEdit())return;
  var rows=SelectedItems();rebuilding=true;try{foreach(var row in rows){var item=CloneItem((SequenceItem)row.Tag);if(column==2)item.Runs=(int)runs.Value;else if(column==4)item.Template.Gap=WaitUnits.ToMilliseconds(cycleGap.Value);else item.TransitionDelay=WaitUnits.ToMilliseconds(gap.Value);row.Tag=item;}Renumber();}finally{rebuilding=false;}if(rows.Length>0)status.Text="已修改 "+rows.Length+" 個範本";
 }
 void CopyItems(){if(cancellation!=null)return;copiedItems=SelectedItems().Select(r=>CloneItem((SequenceItem)r.Tag)).ToList();}
 void PasteItems(){
  if(cancellation!=null||copiedItems.Count==0)return;if(grid.IsCurrentCellInEditMode&&!grid.EndEdit())return;
  if(grid.Rows.Count+copiedItems.Count>1000){status.Text="組合最多可有 1000 個範本。";return;}
  var rows=SelectedItems();int index=rows.Length==0?grid.Rows.Count:rows.Last().Index+1;
  rebuilding=true;try{grid.Rows.Insert(index,copiedItems.Count);for(int i=0;i<copiedItems.Count;i++)grid.Rows[index+i].Tag=CloneItem(copiedItems[i]);Renumber();grid.CurrentCell=grid.Rows[index].Cells[1];grid.ClearSelection();for(int i=0;i<copiedItems.Count;i++)grid.Rows[index+i].Selected=true;}finally{rebuilding=false;}status.Text="已貼上 "+copiedItems.Count+" 個範本";
 }
 void DuplicateItem(){CopyItems();PasteItems();}
 void DeleteItem(){
  if(cancellation!=null)return;var rows=SelectedItems();if(rows.Length==0)return;grid.CancelEdit();int index=rows[0].Index;
  rebuilding=true;try{foreach(var row in rows)grid.Rows.Remove(row);Renumber();grid.ClearSelection();if(grid.Rows.Count>0){index=Math.Min(index,grid.Rows.Count-1);grid.CurrentCell=grid.Rows[index].Cells[1];grid.Rows[index].Selected=true;}}finally{rebuilding=false;}status.Text="已刪除 "+rows.Length+" 個範本";
 }
 void ReorderItems(int[] indexes,int boundary){
  if(cancellation!=null||indexes.Length==0)return;if(grid.IsCurrentCellInEditMode&&!grid.EndEdit())return;
  var original=grid.Rows.Cast<DataGridViewRow>().Select(r=>(SequenceItem)r.Tag).ToList();var selected=new HashSet<SequenceItem>(indexes.Select(i=>original[i]));var ordered=ActionGrid.Reorder(original,indexes,boundary);
  rebuilding=true;grid.SuspendLayout();try{grid.Rows.Clear();foreach(var item in ordered){int i=grid.Rows.Add();grid.Rows[i].Tag=item;}Renumber();int first=ordered.FindIndex(i=>selected.Contains(i));grid.CurrentCell=grid.Rows[first].Cells[1];grid.ClearSelection();foreach(DataGridViewRow row in grid.Rows)row.Selected=selected.Contains((SequenceItem)row.Tag);grid.FirstDisplayedScrollingRowIndex=first;}finally{grid.ResumeLayout();rebuilding=false;}status.Text="已移動 "+indexes.Length+" 個範本";
 }
 protected override bool ProcessCmdKey(ref Message msg,Keys keyData){
  if(grid.ContainsFocus&&!grid.IsCurrentCellInEditMode&&cancellation==null){if(keyData==(Keys.Control|Keys.C)){CopyItems();return true;}if(keyData==(Keys.Control|Keys.V)){PasteItems();return true;}if(keyData==Keys.Delete){DeleteItem();return true;}}
  return base.ProcessCmdKey(ref msg,keyData);
 }
 void Render(DataGridViewRow row){var i=(SequenceItem)row.Tag;row.SetValues(row.Index+1,i.Name,i.Runs,i.Template.Steps.Count,i.Template.Gap/1000m,(i.TransitionDelay??defaultTransition)/1000m);row.ErrorText=MissingSource(i)?"找不到來源檔案："+i.SourcePath:"";row.Cells[1].ToolTipText=row.ErrorText;UpdateEstimate();}
 void Renumber(){foreach(DataGridViewRow row in grid.Rows)Render(row);UpdateEstimate();}
 public void AddItem(SequenceItem item){int index=grid.Rows.Add();grid.Rows[index].Tag=item;Render(grid.Rows[index]);grid.ClearSelection();grid.Rows[index].Selected=true;}
 void AddTemplates(){using(var d=new OpenFileDialog{Filter="動作範本 (*.json)|*.json",Multiselect=true})if(d.ShowDialog(this)==DialogResult.OK){var items=new List<SequenceItem>();foreach(var path in d.FileNames){string data=File.ReadAllText(path);var raw=Json.ReadLoose(data) as Dictionary<string,object>;if(raw==null||raw.ContainsKey("Kind"))throw new Exception("請選擇動作範本，不能加入另一個組合。");var t=Json.Read<Template>(data);var item=new SequenceItem{Name=Path.GetFileNameWithoutExtension(path),SourcePath=Path.GetFullPath(path),Runs=(int)runs.Value,Template=t};SequencePlan.Validate(new SequencePlan{Items=new List<SequenceItem>{item}});items.Add(item);}foreach(var item in items)AddItem(item);}}
 public void SetOuterLoop(int count,int milliseconds){outerRuns.Value=count;outerGap.Value=milliseconds/1000m;}
 public void SetTransition(int value){defaultTransition=value;Renumber();SyncFields();}
 public SequencePlan Current(){if(grid.IsCurrentCellInEditMode&&!grid.EndEdit())throw new Exception("請先修正正在編輯的欄位。");return new SequencePlan{OuterRuns=(int)outerRuns.Value,OuterGap=WaitUnits.ToMilliseconds(outerGap.Value),TransitionDelay=defaultTransition,Items=grid.Rows.Cast<DataGridViewRow>().Select(r=>(SequenceItem)r.Tag).ToList()};}
 void MoveItem(int delta){if(grid.SelectedRows.Count==0)return;int a=grid.SelectedRows[0].Index,b=a+delta;if(b<0||b>=grid.Rows.Count)return;object temp=grid.Rows[a].Tag;grid.Rows[a].Tag=grid.Rows[b].Tag;grid.Rows[b].Tag=temp;Renumber();grid.ClearSelection();grid.Rows[b].Selected=true;}
 void SavePlan(){var plan=Current();SequencePlan.Validate(plan);using(var d=new SaveFileDialog{Filter="範本組合 (*.sequence.json)|*.sequence.json",FileName="我的範本組合.sequence.json"})if(d.ShowDialog(this)==DialogResult.OK){JsonFile.Write(d.FileName,plan);status.Text="組合已儲存（包含各範本內容）";}}
 void LoadPlan(){using(var d=new OpenFileDialog{Filter="範本組合 (*.sequence.json)|*.sequence.json|JSON (*.json)|*.json"})if(d.ShowDialog(this)==DialogResult.OK){string data=File.ReadAllText(d.FileName);var raw=Json.ReadLoose(data) as Dictionary<string,object>;if(raw==null||!raw.ContainsKey("Kind")||Convert.ToString(raw["Kind"])!="MacroSequence")throw new Exception("請選擇範本組合檔案。");var plan=Json.Read<SequencePlan>(data);SequencePlan.Validate(plan);if(grid.Rows.Count>0&&MessageBox.Show(this,"取代目前組合清單？未儲存的修改將遺失。","載入組合",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;SetOuterLoop(plan.OuterRuns,plan.OuterGap);defaultTransition=plan.TransitionDelay;grid.Rows.Clear();foreach(var item in plan.Items)AddItem(item);SyncFields();UpdateEstimate();status.Text="組合已載入";WarnMissingSources();}}
 public void Stop(){if(cancellation!=null)cancellation.Cancel();}
 async Task Run(){if(cancellation!=null)return;try{if(!canRun())throw new Exception("F10 停止熱鍵不可用，請關閉占用熱鍵的程式後重新啟動。");var plan=Current();SequencePlan.Validate(plan);if(plan.Items.Count==0)throw new Exception("請先加入範本。");cancellation=new CancellationTokenSource();foreach(var c in editing)c.Enabled=false;var token=cancellation.Token;await CountdownOverlay.Run(token,s=>status.Text=s);using(var badge=new RunBadge()){badge.Show();await MacroRunner.RunSequence(plan,token,s=>status.Text=s,progress:(name,n)=>badge.SetProgress(name,n),upcoming:badge.SetUpcoming);}status.Text="全部範本執行完成";}catch(OperationCanceledException){status.Text="已停止整個組合";}catch(Exception ex){status.Text="執行中止";MessageBox.Show(this,ex.Message,"範本組合");}finally{if(cancellation!=null){cancellation.Dispose();cancellation=null;}foreach(var c in editing)c.Enabled=true;}}
}

public static class SequenceSession {
 public static string DefaultPath{get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"EvaMacroStudio","last-sequence.json");}}
 public static SequencePlan Load(string path){if(!File.Exists(path))return new SequencePlan();var plan=Json.Read<SequencePlan>(File.ReadAllText(path));SequencePlan.Validate(plan);return plan;}
 public static void Save(string path,SequencePlan plan){SequencePlan.Validate(plan);JsonFile.Write(path,plan,createFolder:true);}
}