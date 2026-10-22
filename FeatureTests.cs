using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

public static class FeatureTests {
 internal static void Check(bool condition,string label){if(!condition)throw new Exception(label);}
 static SequenceItem Item(string name,int runs){return new SequenceItem{Name=name,Runs=runs,Template=new Template{Repeats=77,Gap=17,Steps=new List<Step>{new Step{Type="鍵盤按壓",Value=name,Hold=0,Delay=23}}}};}
 public static void Run(){TestColorScan();
  TestTemplateSaving();
  TestBatchAndSeconds();
  TestLiveUI();
  TestInteraction();
  var plan=new SequencePlan{TransitionDelay=31,Items=new List<SequenceItem>{Item("A",1000),Item("B",1000),Item("C",500)}};
  var trace=new List<string>();var delays=new List<int>();Func<int,CancellationToken,Task> wait=(ms,ct)=>{ct.ThrowIfCancellationRequested();delays.Add(ms);return Task.FromResult(0);};
  MacroRunner.RunSequence(plan,CancellationToken.None,s=>{},(step,ct)=>{trace.Add(step.Value);return Task.FromResult(0);},wait).GetAwaiter().GetResult();
  Check(trace.SequenceEqual(Enumerable.Repeat("A",1000).Concat(Enumerable.Repeat("B",1000)).Concat(Enumerable.Repeat("C",500))),"A1000 B1000 C500 execution order and override");Check(delays.Count(ms=>ms==31)==2&&delays.Count(ms=>ms==17)==2497&&delays.Count(ms=>ms==23)==2500,"Sequence timing");
  using(var stop=new CancellationTokenSource()){trace.Clear();bool canceled=false;try{MacroRunner.RunSequence(plan,stop.Token,s=>{},(step,ct)=>{trace.Add(step.Value);stop.Cancel();return Task.FromResult(0);},wait).GetAwaiter().GetResult();}catch(OperationCanceledException){canceled=true;}Check(canceled&&trace.Count==1,"Cancel halts remaining templates");}
  using(var stop=new CancellationTokenSource()){trace.Clear();bool canceled=false;var shortPlan=new SequencePlan{TransitionDelay=31,Items=new List<SequenceItem>{Item("A",1),Item("B",1)}};try{MacroRunner.RunSequence(shortPlan,stop.Token,s=>{},(step,ct)=>{trace.Add(step.Value);return Task.FromResult(0);},(ms,ct)=>{if(ms==31)stop.Cancel();ct.ThrowIfCancellationRequested();return Task.FromResult(0);}).GetAwaiter().GetResult();}catch(OperationCanceledException){canceled=true;}Check(canceled&&trace.SequenceEqual(new[]{"A"}),"Cancel during transition");}
  trace.Clear();bool failed=false;try{MacroRunner.RunSequence(plan,CancellationToken.None,s=>{},(step,ct)=>{trace.Add(step.Value);throw new Exception("simulated failure");},wait).GetAwaiter().GetResult();}catch{failed=true;}Check(failed&&trace.Count==1,"Action failure aborts sequence");
  var copy=Json.Copy(plan);SequencePlan.Validate(copy);plan.Items[0].Template.Steps[0].Value="Z";Check(copy.Items[0].Template.Steps[0].Value=="A"&&copy.Items[2].Runs==500,"Embedded templates roundtrip");copy.Items[0].Runs=0;bool bad=false;try{SequencePlan.Validate(copy);}catch{bad=true;}Check(bad,"No infinite stage counts");
  Check(KeyPicker.FromKeyData(Keys.A)=="A"&&KeyPicker.FromKeyData(Keys.Control|Keys.Shift|Keys.S)=="Ctrl+Shift+S"&&KeyPicker.FromKeyData(Keys.D1)=="1","Key capture mapping");
  using(var picker=new KeyPicker()){Check(picker.Value=="Space","Single-key default");Check(picker.Controls.OfType<Button>().Count()==1&&!picker.Controls.OfType<Label>().Any(),"Only capture button and default key remain");picker.Value="Ctrl+C";
   var capture=picker.Controls.OfType<Button>().First(b=>b.Text=="捕捉按鍵");var input=picker.Controls.OfType<TextBox>().First();var down=input.GetType().GetMethod("OnKeyDown",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
   capture.PerformClick();down.Invoke(input,new object[]{new KeyEventArgs(Keys.A)});Check(picker.Value=="A"&&!picker.Recording,"Capture actual single-key event");capture.PerformClick();down.Invoke(input,new object[]{new KeyEventArgs(Keys.Control|Keys.S)});Check(picker.Value=="Ctrl+S"&&!picker.Recording,"Capture actual combination event");capture.PerformClick();down.Invoke(input,new object[]{new KeyEventArgs(Keys.Escape)});Check(picker.Value=="Ctrl+S"&&!picker.Recording&&!KeyPicker.AnyRecording,"Capture cancel preserves value");
  }
  using(var main=new MainForm()){var nums=Descendants(main).OfType<NumericUpDown>();Check(nums.Count(n=>n.Maximum==1000000&&n.Value==0)==1&&nums.Count(n=>n.Maximum==86400&&n.Value==1)==2,"1000ms default delay and gap");Check(Descendants(main).OfType<CrosshairDrag>().Count()==1,"New mouse action has draggable crosshair");}
  using(var form=new SequenceForm(()=>true)){foreach(var item in plan.Items)form.AddItem(item);var panel=form.Controls[0];form.Controls.Remove(panel);panel.Size=form.ClientSize;panel.CreateControl();panel.PerformLayout();using(var bitmap=new Bitmap(panel.Width,panel.Height)){panel.DrawToBitmap(bitmap,new Rectangle(Point.Empty,panel.Size));bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"preview-sequence.png"));}panel.Dispose();}
  File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"feature-test-result.txt"),"PASS: exact A1000/B1000/C500 order; repeat override; action, cycle and transition intervals; cancellation within action and transition; failure abort; embedded snapshot roundtrip; positive repeat validation; single-key defaults; capture mapping; common-key replacement; 1000ms defaults; new crosshair control. Input execution mocked; no desktop input sent.");
 }
 static void TestTemplateSaving(){
  var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
  Func<MainForm,string,object[],object> invoke=(form,name,args)=>typeof(MainForm).GetMethod(name,flags).Invoke(form,args);
  Func<MainForm,bool> dirty=form=>(bool)invoke(form,"HasUnsavedChanges",new object[0]);
  string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"save-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);string path=Path.Combine(folder,"original.json");
  using(var form=new MainForm()){
   Check(!dirty(form),"New empty template is clean");var a=new Step{Type="滑鼠點擊",Value="左鍵",X=12,Y=34,Hold=50,Delay=500,Notes="original"};invoke(form,"AddStep",new object[]{a});Check(dirty(form),"New action is unsaved");
   invoke(form,"SaveToPath",new object[]{path});Check(File.Exists(path)&&!dirty(form),"Save establishes baseline");a.Notes="changed";Check(dirty(form),"Notes change detected");
   object[] hotkey={new Message(),Keys.Control|Keys.S};Check((bool)invoke(form,"ProcessCmdKey",hotkey),"Ctrl+S handled");var saved=Json.Read<Template>(File.ReadAllText(path));Check(saved.Steps[0].Notes=="changed"&&!dirty(form),"Ctrl+S overwrites original and clears dirty state");
   a.Notes="temporary";a.Notes="changed";Check(!dirty(form),"Reverting changes restores clean state");a.Delay=1000;bool failed=false;try{invoke(form,"SaveToPath",new object[]{Path.Combine(folder,"missing","bad.json")});}catch(System.Reflection.TargetInvocationException){failed=true;}Check(failed&&dirty(form),"Failed save preserves unsaved state");
   invoke(form,"SaveCurrentTemplate",new object[0]);Check(!dirty(form)&&Json.Read<Template>(File.ReadAllText(path)).Steps[0].Delay==1000,"Failed save does not change target path");
   var grid=Descendants(form).OfType<ActionGrid>().Single();grid.Rows.Clear();Check(dirty(form),"Deleting all actions remains unsaved");
  }
  using(var loaded=new MainForm()){
   var template=Json.Read<Template>(File.ReadAllText(path));foreach(var step in template.Steps)invoke(loaded,"AddStep",new object[]{step});invoke(loaded,"MarkSaved",new object[]{path});Check(!dirty(loaded)&&(bool)invoke(loaded,"ConfirmLeave",new object[0]),"Loaded template can leave without warning");template.Steps[0].X=999;Check(dirty(loaded),"Loaded template edits detected");invoke(loaded,"SaveCurrentTemplate",new object[0]);Check(Json.Read<Template>(File.ReadAllText(path)).Steps[0].X==999,"Loaded template saves to original path");
  }
  File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"save-test-result.txt"),"PASS: clean/new/loaded states, Ctrl+S original path overwrite, notes and deletion detection, revert-to-clean, failed save retains dirty state and target, loaded template save. No desktop input sent.");
 }
 static void TestBatchAndSeconds(){
  Check(WaitUnits.ToMilliseconds(1)==1000&&WaitUnits.ToMilliseconds(.5m)==500&&WaitUnits.ToMilliseconds(.001m)==1&&WaitUnits.ToMilliseconds(0)==0,"Seconds conversion exact");
  foreach(decimal n in new[]{-.1m,.0001m,86400.001m}){bool rejected=false;try{WaitUnits.ToMilliseconds(n);}catch{rejected=true;}Check(rejected,"Seconds range and precision");}
  var a=new Step{Type="滑鼠點擊",Value="左鍵",X=10,Y=20,Hold=50,Delay=500,Notes="A"};var b=new Step{Type="滑鼠點擊",Value="右鍵",X=30,Y=40,Hold=80,Delay=1250,Notes="B"};
  var changed=new BatchPatch{Delay=2000}.Apply(new[]{a,b});Check(changed.All(s=>s.Delay==2000)&&changed[0].X==10&&changed[1].X==30&&changed[1].Hold==80&&changed[1].Value=="右鍵"&&changed[1].Notes=="B"&&b.Delay==1250,"Batch only selected fields change");
  changed=new BatchPatch{SetNotes=true,Notes=""}.Apply(new[]{a,b});Check(changed.All(s=>s.Notes=="")&&a.Notes=="A","Batch clear notes without source mutation");
  bool mixed=false;try{new BatchPatch{Delay=1000}.Apply(new[]{a,new Step{Type="等待",Delay=1000}});}catch{mixed=true;}Check(mixed,"Mixed types rejected");bool bad=false;try{new BatchPatch{Hold=-1}.Apply(new[]{a,b});}catch{bad=true;}Check(bad&&a.Hold==50&&b.Hold==80,"Invalid batch atomicity");
  Check(WaitUnits.RowColor("滑鼠點擊")!=WaitUnits.RowColor("鍵盤按壓")&&WaitUnits.RowColor("鍵盤按壓")!=WaitUnits.RowColor("等待"),"Distinct row colors");
  using(var editor=new StepEditor(a)){Check(editor.BuildResult().Delay==500,"Legacy milliseconds roundtrip in seconds editor");}
  using(var dialog=new BatchEditor(new[]{a,b})){var panel=dialog.Controls[0];dialog.Controls.Remove(panel);panel.Size=dialog.ClientSize;panel.CreateControl();using(var bitmap=new Bitmap(panel.Width,panel.Height)){panel.DrawToBitmap(bitmap,new Rectangle(Point.Empty,panel.Size));bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"preview-batch.png"));}panel.Dispose();}
  File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"batch-test-result.txt"),"PASS: seconds conversion and precision, old milliseconds roundtrip, selective batch patch, note clearing, mixed-type rejection, invalid batch atomicity, distinct row colors. No desktop input sent.");
 }
 static void TestLiveUI(){
  var estimatePlan=new SequencePlan{TransitionDelay=500,Items=new List<SequenceItem>{
   new SequenceItem{Name="A",Runs=2,Template=new Template{Gap=100,Steps=new List<Step>{new Step{Type="鍵盤按壓",Value="A",Hold=50,Delay=200},new Step{Type="等待",Hold=999,Delay=300}}}},
   new SequenceItem{Name="B",Runs=1,Template=new Template{Gap=999,Steps=new List<Step>{new Step{Type="等待",Delay=250}}}}
  }};
  Check(SequenceForm.EstimateMilliseconds(estimatePlan)==4953&&SequenceForm.FormatDuration(4953)=="5 秒","Estimate holds, waits, cycle gaps, transitions and countdown");
  Check(SequenceForm.EstimateMilliseconds(new SequencePlan())==0,"Empty sequence estimate");
  estimatePlan.Items[0].TransitionDelay=1750;estimatePlan.Items[1].TransitionDelay=9000;
  Check(SequenceForm.EstimateMilliseconds(estimatePlan)==6203,"Per-template transition estimate excludes final transition");
  var transitionWaits=new List<int>();MacroRunner.RunSequence(estimatePlan,CancellationToken.None,s=>{},(a,ct)=>Task.FromResult(0),(ms,ct)=>{transitionWaits.Add(ms);return Task.FromResult(0);}).GetAwaiter().GetResult();
  Check(transitionWaits.Contains(1750)&&!transitionWaits.Contains(9000),"Runner uses per-template transition");
  var persisted=Json.Copy(estimatePlan);Check(persisted.Items[0].TransitionDelay==1750,"Per-template transition persists");
  var repeated=new SequencePlan{OuterRuns=2,OuterGap=700,Items=new List<SequenceItem>{Item("A",1),Item("B",1)},TransitionDelay=300};
  var repeatedTrace=new List<string>();var repeatedWaits=new List<int>();
  MacroRunner.RunSequence(repeated,CancellationToken.None,s=>{},(a,ct)=>{repeatedTrace.Add(a.Value);return Task.FromResult(0);},(ms,ct)=>{repeatedWaits.Add(ms);return Task.FromResult(0);}).GetAwaiter().GetResult();
  Check(string.Join(",",repeatedTrace)=="A,B,A,B"&&repeatedWaits.Count(ms=>ms==700)==1&&repeatedWaits.Count(ms=>ms==300)==2,"Whole sequence loops in order with outer wait only between cycles");
  Check(SequenceForm.EstimateMilliseconds(repeated)==4396,"Repeated sequence estimate counts initial countdown once");
  repeated.OuterRuns=0;Check(SequenceForm.EstimateMilliseconds(repeated)==-1,"Continuous sequence estimate");
  using(var stop=new CancellationTokenSource()){int actions=0;bool stopped=false;try{MacroRunner.RunSequence(repeated,stop.Token,s=>{},(a,ct)=>{if(++actions==3)stop.Cancel();return Task.FromResult(0);},(ms,ct)=>{ct.ThrowIfCancellationRequested();return Task.FromResult(0);}).GetAwaiter().GetResult();}catch(OperationCanceledException){stopped=true;}Check(stopped&&actions==3,"Continuous whole sequence cancellation");}
  var legacyPlan=Json.Read<SequencePlan>("{\"Kind\":\"MacroSequence\",\"Version\":1,\"Items\":[]}");
  Check(legacyPlan.OuterRuns==1&&legacyPlan.OuterGap==1000,"Old sequence defaults to one pass");
  using(var iconStream=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("EvaMacroStudio.AppIcon")){Check(iconStream!=null&&AppIdentity.Icon.Width>0,"Embedded application icon");}
  var original=new Step{Type="滑鼠點擊",Value="左鍵",X=20,Y=30,Hold=50,Delay=1000,Notes="before"};Check(CellEdits.Apply(original,6,"1.5").Delay==1500&&original.Delay==1000,"Cell edit clone isolation");Check(CellEdits.Apply(original,2,"-200").X==-200&&CellEdits.Apply(original,7,"測試 123").Notes=="測試 123","Cell coordinates and notes");foreach(string invalid in new[]{"-1","1.0001","abc","999999999999"}){bool rejected=false;try{CellEdits.Apply(original,6,invalid);}catch{rejected=true;}Check(rejected,"Reject invalid cell numeric input");}Check(!CellEdits.CanEdit(original,0)&&!CellEdits.CanEdit(new Step{Type="等待"},2),"Nonapplicable cells read only");
  var forecasts=new List<string>();var forecastTemplate=new Template{Gap=40,Steps=new List<Step>{new Step{Type="鍵盤按壓",Value="A",Hold=20,Delay=100},new Step{Type="等待",Delay=200}}};
  MacroRunner.RunTemplate(forecastTemplate,2,CancellationToken.None,s=>{},(a,ct)=>Task.FromResult(0),(ms,ct)=>Task.FromResult(0),upcoming:(a,ms)=>forecasts.Add((a==null?"完成":a.Type)+":"+ms)).GetAwaiter().GetResult();
  Check(forecasts.Take(4).SequenceEqual(new[]{"等待:120","等待:100","鍵盤按壓:241","鍵盤按壓:241"})&&forecasts.Last().StartsWith("完成:"),"Upcoming action includes hold, delay and cycle gap");
  forecasts.Clear();MacroRunner.RunSequence(new SequencePlan{TransitionDelay=70,Items=new List<SequenceItem>{Item("A",1),Item("B",1)}},CancellationToken.None,s=>{},(a,ct)=>Task.FromResult(0),(ms,ct)=>Task.FromResult(0),upcoming:(a,ms)=>forecasts.Add((a==null?"完成":a.Value)+":"+ms)).GetAwaiter().GetResult();
  Check(forecasts.Contains("B:94")&&forecasts.Contains("B:70")&&forecasts.Last().StartsWith("完成:"),"Upcoming action crosses template boundary");
  Check(RunBadge.DescribeUpcoming(new Step{Type="等待"},1250).Contains("1.3 sec")&&RunBadge.DescribeUpcoming(null,0).Contains("即將完成"),"Countdown formatting and final action");
  var remaining=new List<string>();var plan=new SequencePlan{Items=new List<SequenceItem>{Item("A",2),Item("B",1)}};MacroRunner.RunSequence(plan,CancellationToken.None,s=>{},(step,ct)=>Task.FromResult(0),(ms,ct)=>Task.FromResult(0),(name,n)=>remaining.Add(name+":"+n)).GetAwaiter().GetResult();Check(remaining.SequenceEqual(new[]{"A:2","A:1","A:1","A:0","B:1","B:0"}),"Template name and remaining cycles");Check(RunBadge.Describe("A",-1).Contains("持續循環"),"Unlimited progress display");
  using(var main=new MainForm()){var add=typeof(MainForm).GetMethod("AddStep",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);add.Invoke(main,new object[]{original});var grid=Descendants(main).OfType<ActionGrid>().Single();var panel=main.Controls[0];main.Controls.Remove(panel);panel.Size=main.ClientSize;panel.CreateControl();grid.CurrentCell=grid.Rows[0].Cells[6];Check(grid.BeginEdit(true),"Begin inline edit");((TextBox)grid.EditingControl).Text="2.3";Check(grid.EndEdit()&&((Step)grid.Rows[0].Tag).Delay==2300,"Inline UI edit commits to model");grid.CurrentCell=grid.Rows[0].Cells[6];grid.BeginEdit(true);((TextBox)grid.EditingControl).Text="-5";bool ended=grid.EndEdit();Check(((Step)grid.Rows[0].Tag).Delay==2300,"Invalid inline edit leaves saved value unchanged");grid.CancelEdit();panel.Dispose();}
  using(var sequence=new SequenceForm(()=>true)){
   sequence.AddItem(Item("initial",2));var panel=sequence.Controls[0];sequence.Controls.Remove(panel);panel.Size=sequence.ClientSize;panel.CreateControl();
   var grid=Descendants(panel).OfType<DataGridView>().Single();var doubleClick=typeof(ActionGrid).GetMethod("OnMouseDoubleClick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
   int[] columns={1,2,4,5};string[] values={"renamed","17","1.25","2.5"};
   for(int i=0;i<columns.Length;i++){var rect=grid.GetCellDisplayRectangle(columns[i],0,false);doubleClick.Invoke(grid,new object[]{new MouseEventArgs(MouseButtons.Left,2,rect.Left+5,rect.Top+5,0)});Application.DoEvents();Check(grid.IsCurrentCellInEditMode,"Sequence double click starts column "+columns[i]);((TextBox)grid.EditingControl).Text=values[i];Check(grid.EndEdit(),"Sequence cell commits "+columns[i]);}
   var item=sequence.Current().Items[0];Check(item.Name=="renamed"&&item.Runs==17&&item.Template.Gap==1250&&item.TransitionDelay==2500,"All editable sequence cells update model");panel.Dispose();
  }
  using(var sequence=new SequenceForm(()=>true)){
   foreach(string name in new[]{"A","B","C","D"})sequence.AddItem(Item(name,2));
   var grid=Descendants(sequence).OfType<ActionGrid>().Single();
   Action<string,object[]> invoke=(name,args)=>typeof(SequenceForm).GetMethod(name,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(sequence,args);
   grid.ClearSelection();grid.Rows[0].Selected=true;grid.Rows[2].Selected=true;invoke("CopyItems",new object[0]);invoke("PasteItems",new object[0]);
   Check(string.Join(",",sequence.Current().Items.Select(i=>i.Name))=="A,B,C,A,C,D"&&grid.SelectedRows.Count==2,"Sequence multiple paste order");
   sequence.Current().Items[3].Template.Steps[0].Notes="copy";Check(sequence.Current().Items[0].Template.Steps[0].Notes!="copy","Sequence clipboard deep copy");
   invoke("DeleteItem",new object[0]);Check(string.Join(",",sequence.Current().Items.Select(i=>i.Name))=="A,B,C,D","Sequence multi delete");
   invoke("ReorderItems",new object[]{new[]{0,2},4});Check(string.Join(",",sequence.Current().Items.Select(i=>i.Name))=="B,D,A,C"&&grid.SelectedRows.Count==2,"Sequence multi reorder preserves selection");

   var cycleGap=(NumericUpDown)typeof(SequenceForm).GetField("cycleGap",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(sequence);cycleGap.Value=1.5m;
   Check(sequence.Current().Items.Skip(2).All(i=>i.Template.Gap==1500&&i.Runs==2)&&sequence.Current().Items[0].Template.Gap==17,"Selective batch edit only selected templates");
   grid.SelectAll();invoke("DeleteItem",new object[0]);Check(sequence.Current().Items.Count==0,"Delete entire sequence");
  }
  using(var badge=new RunBadge())using(var bitmap=new Bitmap(badge.Width,badge.Height)){badge.SetProgress("A 範本",999);badge.SetUpcoming(new Step{Type="鍵盤按壓"},3250);badge.DrawToBitmap(bitmap,new Rectangle(Point.Empty,badge.Size));bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"preview-running.png"));}
  File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"live-ui-test-result.txt"),"PASS: inline numeric UI commit, invalid numeric rejection without mutation, coordinate/notes cell edits, protected cells, sequence name/remaining progress, continuous-loop label. No desktop input sent.");
 }
 static void TestInteraction(){
  var list=new[]{"A","B","C","D","E"};
  Check(ActionGrid.Reorder(list,new[]{1},5).SequenceEqual(new[]{"A","C","D","E","B"}),"Drag single down");
  Check(ActionGrid.Reorder(list,new[]{3},0).SequenceEqual(new[]{"D","A","B","C","E"}),"Drag single up");
  Check(ActionGrid.Reorder(list,new[]{2,1},5).SequenceEqual(new[]{"A","D","E","B","C"}),"Multi-drag maintains original order");
  Check(ActionGrid.Reorder(list,new[]{2,3},0).SequenceEqual(new[]{"C","D","A","B","E"}),"Multi-drag up");
  Check(ActionGrid.Reorder(list,new[]{1,2},2).SequenceEqual(list),"Drop within selected block is unchanged");
  Check(ActionGrid.Reorder(list,new[]{0,2,4},5).SequenceEqual(new[]{"B","D","A","C","E"}),"Noncontiguous selection");
  var shown=new List<int>();var waits=new List<int>();CountdownOverlay.Count(CancellationToken.None,n=>shown.Add(n),(ms,ct)=>{waits.Add(ms);return Task.FromResult(0);}).GetAwaiter().GetResult();Check(shown.SequenceEqual(new[]{3,2,1})&&waits.SequenceEqual(new[]{1000,1000,1000}),"321 countdown timing");
  using(var stop=new CancellationTokenSource()){shown.Clear();bool canceled=false;try{CountdownOverlay.Count(stop.Token,n=>{shown.Add(n);if(n==2)stop.Cancel();},(ms,ct)=>{ct.ThrowIfCancellationRequested();return Task.FromResult(0);}).GetAwaiter().GetResult();}catch(OperationCanceledException){canceled=true;}Check(canceled&&shown.SequenceEqual(new[]{3,2}),"Countdown cancellation");}
  string note="測試 123 !@#\r\n第二行 😀 \"quoted\"";var step=new Step{Type="滑鼠點擊",Value="左鍵",X=12,Y=34,Delay=1000,Notes=note};Check(Json.Copy(step).Notes==note&&MainForm.CopyStep(step).Notes==note,"Notes copied and persisted exactly");
  using(var editor=new StepEditor(step)){Check(editor.BuildResult().Notes==note,"Notes loaded for editing");Descendants(editor).OfType<TextBox>().First(t=>t.Multiline).Text="修改\n456";Check(editor.BuildResult().Notes=="修改\n456"&&step.Notes==note,"Edit notes without mutating original");var panel=editor.Controls[0];editor.Controls.Remove(panel);panel.Size=editor.ClientSize;panel.CreateControl();using(var bitmap=new Bitmap(panel.Width,panel.Height)){panel.DrawToBitmap(bitmap,new Rectangle(Point.Empty,panel.Size));bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"preview-notes-edit.png"));}panel.Dispose();}
  using(var main=new MainForm()){
   var grid=Descendants(main).OfType<ActionGrid>().Single();var add=typeof(MainForm).GetMethod("AddStep",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);for(int i=0;i<5;i++)add.Invoke(main,new object[]{new Step{Type="滑鼠點擊",Value="左鍵",X=100+i,Y=200,Delay=1000,Notes="備註 "+i}});
   var reorder=typeof(MainForm).GetMethod("ReorderActions",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);reorder.Invoke(main,new object[]{new[]{1,2},5});var steps=grid.Rows.Cast<DataGridViewRow>().Select(r=>(Step)r.Tag).ToList();Check(steps.Select(s=>s.X).SequenceEqual(new[]{100,103,104,101,102})&&steps.Select(s=>s.Sequence).SequenceEqual(new[]{1,2,3,4,5}),"UI reorder renumbers");Check(grid.SelectedRows.Count==2&&steps[3].Notes=="備註 1"&&MainForm.BuildMarkers(steps)[3].Label=="4","UI reorder preserves selection, notes and marker numbering");
  }
  using(var overlay=new CountdownOverlay())using(var bitmap=new Bitmap(overlay.Width,overlay.Height)){overlay.DrawToBitmap(bitmap,new Rectangle(Point.Empty,overlay.Size));bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"preview-countdown.png"));}
  File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"interaction-test-result.txt"),"PASS: single/multiple/noncontiguous reorder, no-op drop, original relative order, grid selection retention, marker renumbering, multiline Unicode notes persistence/copy/edit isolation, 3-2-1 countdown timing and cancellation. No desktop input was sent.");
 }
 static IEnumerable<Control> Descendants(Control c){foreach(Control child in c.Controls){yield return child;foreach(var d in Descendants(child))yield return d;}}

 // 顏色掃描小工具的測試。一律不顯示視窗、不註冊熱鍵、不動使用者的設定檔，
 // 只在執行檔旁產生預覽圖與測試資料。
 static int[] Canvas(int width, int height, Color background, Rectangle patch, Color fill)
 {
  var pixels = new int[width * height]; int back = background.ToArgb(), front = fill.ToArgb();
  for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) pixels[y * width + x] = patch.Contains(x, y) ? front : back;
  return pixels;
 }
 static void TestColorScan()
 {
  Check(ColorRule.Parse("#FF0000") == Color.FromArgb(255, 255, 0, 0), "Hex color parsing");
  Check(ColorRule.Parse("ff0000") == ColorRule.Parse("#f00"), "Short hex and missing hash");
  Check(ColorRule.Format(ColorRule.Parse("Lime")) == "#00FF00", "Named color parsing and formatting");
  foreach (string bad in new[] { "", "#12", "xyzxyz", "#GGGGGG" }) { bool rejected = false; try { ColorRule.Parse(bad); } catch { rejected = true; } Check(rejected, "Invalid color accepted: " + bad); }
  Check(ColorScanner.Difference(Color.FromArgb(255, 100, 100, 100).ToArgb(), Color.FromArgb(120, 100, 100)) == 20, "Channel difference uses the largest gap");

  // 自動對比色：必須和目標色差得夠遠，否則掃描會吸到自己畫的高亮而閃動。
  foreach (var sample in new[] { Color.Red, Color.Lime, Color.Blue, Color.White, Color.Black, Color.FromArgb(128, 128, 128), Color.FromArgb(0, 0, 128), Color.FromArgb(214, 64, 64) })
  {
   var contrast = ColorRule.Contrast(sample);
   Check(ColorScanner.Difference(contrast.ToArgb(), sample) > 100, "Contrast colour is far from " + ColorRule.Format(sample));
   Check(contrast.A == 255, "Contrast colour is opaque");
  }
  Check(ColorRule.Contrast(Color.Red).B > 150 && ColorRule.Contrast(Color.Red).R < 60, "Red picks a cyan-side highlight");
  Check(ColorRule.Contrast(Color.Lime).R > 150 && ColorRule.Contrast(Color.Lime).G < 60, "Green picks a magenta-side highlight");
  Check(ColorRule.Contrast(Color.Black) != ColorRule.Contrast(Color.White), "Near-greys split by brightness");
  Check(ColorRule.Contrast(Color.FromArgb(24, 24, 24)) == ColorRule.Contrast(Color.Black), "Near-greys share the dark fallback");

  var target = Color.FromArgb(200, 40, 40);
  var pixels = Canvas(32, 32, Color.White, new Rectangle(8, 8, 8, 8), target);
  var result = ColorScanner.Scan(pixels, 32, 32, new[] { target }, new[] { 16 }, 2, 4);
  Check(result.Counts[0] == 4 && result.Hits.Count == 2, "Matched block merges into per-row runs");
  Check(result.Hits.All(h => h.Bounds.Width == 8 && h.Bounds.Height == 4), "Runs cover the patch width");
  Check(result.Centers[0] == new Point(12, 12), "Centroid of the matched patch");
  Check(ColorScanner.Scan(pixels, 32, 32, new[] { target }, new[] { 0 }, 2, 4).Counts[0] == 4, "Exact color matches with zero tolerance");
  Check(ColorScanner.Scan(pixels, 32, 32, new[] { Color.FromArgb(0, 0, 255) }, new[] { 10 }, 2, 4).Total == 0, "Unrelated color finds nothing");
  // 上方色碼組的容差涵蓋下方的目標色時，下方那組就完全沒有命中（介面提示文字說明的就是這件事）。
  Check(ColorScanner.Scan(pixels, 32, 32, new[] { Color.White, target }, new[] { 255, 16 }, 2, 4).Counts[1] == 0, "A wide first rule swallows the rules below it");
  Check(ColorScanner.Scan(pixels, 32, 32, new[] { target, Color.White }, new[] { 16, 255 }, 2, 4).Counts[0] == 4, "Reordering gives the narrower rule its hits back");
  Check(ColorScanner.Scan(pixels, 32, 32, new[] { target }, new[] { 16 }, 8, 4).Counts[0] < 4, "Coarse sampling misses cells");
  Check(ColorScanner.Scan(pixels, 32, 32, new Color[0], new int[0], 2, 4).Total == 0, "Empty rule list scans nothing");
  bool mismatched = false; try { ColorScanner.Scan(pixels, 32, 32, new[] { target }, new int[0], 2, 4); } catch { mismatched = true; }
  Check(mismatched, "Rule and tolerance counts must match");
  var edge = ColorScanner.Scan(Canvas(10, 10, Color.White, new Rectangle(0, 0, 10, 10), target), 10, 10, new[] { target }, new[] { 0 }, 1, 4);
  Check(edge.Hits.All(h => h.Bounds.Right <= 10 && h.Bounds.Bottom <= 10), "Blocks clip to the scan area");

  using (var overlay = new ScanOverlay())
  {
   overlay.ScanArea = new Rectangle(300, 200, 320, 240);
   Check(overlay.ScanArea == new Rectangle(300, 200, 320, 240), "Scan area excludes the drag band");
   Check(overlay.Bounds == new Rectangle(300 - ScanOverlay.Band, 200 - ScanOverlay.Band, 320 + ScanOverlay.Band * 2, 240 + ScanOverlay.Band * 2), "Chrome sits outside the scanned pixels");
   var frozen = overlay.ScanArea;
   Check(!overlay.BeginDrag(new Point(ScanOverlay.Band / 2, overlay.ClientSize.Height / 2)), "A locked box refuses to start a drag");
   Check(!overlay.DragTo(new Size(40, -25)) && overlay.ScanArea == frozen, "A locked box cannot be moved by the mouse at all");
   overlay.Locked = false;
   Check(overlay.BeginDrag(new Point(ScanOverlay.Band / 2, overlay.ClientSize.Height / 2)), "Edit mode accepts a border drag");
   Check(overlay.DragTo(new Size(40, -25)) && overlay.ScanArea == new Rectangle(340, 175, 320, 240), "Border drag moves the box without resizing");
   overlay.EndDrag();
   Check(overlay.ZoneAt(new Point(2, 2)) == 1 && overlay.ZoneAt(new Point(overlay.ClientSize.Width - 2, overlay.ClientSize.Height - 2)) == 4 && overlay.ZoneAt(new Point(ScanOverlay.Band / 2, overlay.ClientSize.Height / 2)) == 5, "Corner grips and move band");
   var bounds = new Rectangle(100, 100, 200, 200);
   Check(ScanOverlay.Transform(bounds, 4, new Size(30, 40)) == new Rectangle(100, 100, 230, 240), "Bottom-right grip resizes");
   Check(ScanOverlay.Transform(bounds, 1, new Size(30, 40)) == new Rectangle(130, 140, 170, 160), "Top-left grip moves the origin");
   Check(ScanOverlay.Transform(bounds, 1, new Size(9000, 9000)).Width == ScanOverlay.MinSide + ScanOverlay.Band * 2, "Resize keeps the minimum size");
   Check(ScanOverlay.Transform(bounds, 5, new Size(-15, 7)) == new Rectangle(85, 107, 200, 200), "Move keeps the size");
   var scanned = Color.FromArgb(214, 64, 64);
   overlay.Highlights = new List<Color> { ColorRule.Contrast(scanned) };
   // 閃爍就是「畫」與「不畫」交替，只有一個高亮顏色。
   // 掃描永遠只在不畫的那一相位進行，所以讀到的畫面上沒有我們自己的東西——
   // 這是「不排除螢幕擷取也不會掃到自己」的關鍵性質。
   Check(!ScanOverlay.NeedsBlank(0, false), "With no hits nothing covers the region, so no blank is needed");
   Check(ScanOverlay.NeedsBlank(3, false), "With hits on screen the box must blank before scanning");
   Check(!ScanOverlay.NeedsBlank(3, true), "Already blank means the next step is the scan itself");
   overlay.SeedHits(new ScanHit { Rule = 0, Bounds = new Rectangle(0, 0, 4, 4) });
   Check(!overlay.Blanked && overlay.Phase.Count == 1 && overlay.Phase[0] == ColorRule.Contrast(scanned), "The lit phase paints the single highlight colour");
   overlay.Step();
   Check(overlay.Blanked && overlay.Phase.Count == 0, "The dark phase paints nothing, so the original colour shows through");
   // 閃爍開啟時亮暗各佔一個完整間隔（週期加倍、掃描頻率減半）；關閉時只留最小空白。
   const int slow = 800;
   overlay.Interval = slow;
   overlay.Blink = true;
   Check(overlay.BlankLength == slow && overlay.LitLength == slow, "Blinking gives each phase a whole interval");
   overlay.Blink = false;
   Check(overlay.BlankLength == ScanOverlay.BlankWindow && overlay.LitLength == slow - ScanOverlay.BlankWindow, "A steady highlight keeps only the minimum blank the scan needs");
   Check(overlay.BlankLength + overlay.LitLength == overlay.Interval, "Without blinking the cycle is exactly one scan interval");
   // 間隔的預設值同時也是下限，而且必須容得下暗相位。
   Check(ScanOverlay.DefaultInterval > ScanOverlay.BlankWindow, "The interval floor always leaves room for the blank phase");
   overlay.Interval = 1;
   Check(overlay.Interval == ScanOverlay.DefaultInterval, "Too small an interval is clamped to the floor");
  }
  // 吸色色票永遠和游標錯開，所以不會蓋住正在取樣的那一個像素。
  var bubbleSize = ColorBubble.Preferred; var screenArea = new Rectangle(0, 0, 1920, 1080);
  Check(ColorBubble.Place(screenArea, new Point(400, 400), bubbleSize) == new Point(424, 424), "Colour bubble sits below-right of the cursor");
  Check(ColorBubble.Place(screenArea, new Point(1910, 1070), bubbleSize).X == 1910 - 24 - bubbleSize.Width, "Colour bubble flips away from the screen edge");
  var placed = ColorBubble.Place(screenArea, new Point(4, 4), bubbleSize);
  Check(placed.X >= 0 && placed.Y >= 0, "Colour bubble stays on screen");
  Check(!new Rectangle(placed, bubbleSize).Contains(new Point(4, 4)), "Colour bubble never covers the sampled pixel");
  using (var idle = new ColorPicker(null)) Check(!idle.Running, "A picker installs no hook until it starts");

  var profile = new ScanProfile { X = 10, Y = 20, Width = 200, Height = 150, Interval = ScanOverlay.DefaultInterval, Sample = 2, Block = 4, Rules = new List<ColorRule> { new ColorRule { Target = "#123456", Tolerance = 30 } } };
  ScanProfile.Validate(profile);
  var copy = Json.Copy(profile);
  ScanProfile.Validate(copy); Check(copy.Rules[0].Target == "#123456" && copy.Rules[0].Tolerance == 30 && copy.Width == 200, "Scan profile roundtrip");
  foreach (var broken in new[] { new ScanProfile { Interval = ScanOverlay.DefaultInterval - 1 }, new ScanProfile { Sample = 0 }, new ScanProfile { Block = 0 }, new ScanProfile { Width = 4, Height = 4 } })
  {
   bool rejected = false; try { ScanProfile.Validate(broken); } catch { rejected = true; }
   Check(rejected, "Invalid scan setting accepted");
  }
  {
   bool rejected = false; var tooMany = new ScanProfile(); for (int i = 0; i < 21; i++) tooMany.Rules.Add(new ColorRule());
   try { ScanProfile.Validate(tooMany); } catch { rejected = true; }
   Check(rejected, "Rule count limit");
  }
  string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "save-tests-scan-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
  string file = Path.Combine(folder, "scan-profile.json");
  Check(ScanSession.Load(file).Rules.Count == 1, "Missing profile falls back to a centered default");
  ScanSession.Save(file, profile); Check(ScanSession.Load(file).Rules[0].Target == "#123456", "Saved profile reloads");
  profile.Rules[0].Tolerance = 99; ScanSession.Save(file, profile); Check(ScanSession.Load(file).Rules[0].Tolerance == 99, "Profile overwrite keeps the latest values");
  File.WriteAllText(file, "{\"Kind\":\"Other\"}"); bool wrongKind = false; try { ScanSession.Load(file); } catch { wrongKind = true; }
  Check(wrongKind, "Foreign profile file rejected");
  // 自動保存的位置是程式自己管理的，第一次執行時資料夾還不存在，所以要自己建起來。
  // 使用者指定的另存位置則相反——資料夾不存在就該失敗，那條路由 TestTemplateSaving 驗證。
  string nested = Path.Combine(folder, "nested", "scan-profile.json");
  ScanSession.Save(nested, profile);
  Check(File.Exists(nested), "Auto-saved settings create their own folder when missing");

  using (var studio = new ScanStudioForm(profile))
  {
   Check(studio.Rows().Count() == 1, "Profile rules load into rows");
   var added = studio.AddRule(new ColorRule { Target = "#0088FF", Tolerance = 12 });
   Check(studio.Rows().Count() == 2, "Add button appends a rule row");
   Check(added.Controls.OfType<TextBox>().Count() == 1, "A rule row only asks for the target colour");
   Check(added.Controls.OfType<NumericUpDown>().Count() == 1, "Each rule row carries its own tolerance");
   Check(added.Highlight == ColorRule.Contrast(ColorRule.Parse("#0088FF")), "The row reports the automatic contrast highlight");
   // 換成工具視窗的中文字型後，按鈕必須跟著長大，文字不能被擠壓。
   added.PerformLayout();
   foreach (var button in added.Controls.OfType<Button>())
   {
    var needed = TextRenderer.MeasureText(button.Text, button.Font);
    Check(button.Width >= needed.Width + button.Padding.Horizontal, "Row button fits its label: " + button.Text);
   }
   Check(studio.overlay.Targets.Count == 2 && studio.overlay.Highlights[1] == added.Highlight && studio.overlay.Tolerances[1] == 12, "Rows feed the overlay");
   added.Target.Text = "不是色碼";
   Check(studio.overlay.Targets.Count == 1 && studio.Current().Rules.Count == 1, "Half-typed colours are skipped, not fatal");
   added.Target.Text = "#0088FF"; Check(studio.overlay.Targets.Count == 2, "Fixing the colour restores the rule");
   studio.areaX.Value = 640; studio.areaY.Value = 360; studio.areaW.Value = 200; studio.areaH.Value = 120;
   Check(studio.overlay.ScanArea == new Rectangle(640, 360, 200, 120), "Area fields drive the overlay");
   var box = studio.overlay; int mid = box.ClientSize.Height / 2;
   Check(box.BeginDrag(new Point(ScanOverlay.Band / 2, mid)) && box.DragTo(new Size(10, 10)), "Edit mode accepts a border drag");
   box.EndDrag();
   Check(studio.areaX.Value == 650 && studio.areaY.Value == 370 && studio.Current().X == 650, "Border drag writes back to the fields");
   studio.SetLocked(true); Check(studio.overlay.Locked && studio.lockButton.Text == "編輯掃描範圍", "Lock toggle text");
   Check(studio.GeometryFields().All(f => !f.Enabled) && !studio.centerButton.Enabled, "Lock freezes the coordinate and size fields");
   var pinned = box.ScanArea;
   Check(!box.BeginDrag(new Point(ScanOverlay.Band / 2, mid)) && !box.DragTo(new Size(5, -5)), "A locked box refuses every drag");
   Check(box.ScanArea == pinned && studio.areaX.Value == 650 && studio.areaY.Value == 370, "A locked box stays exactly where it was");
   studio.SetLocked(false); Check(!studio.overlay.Locked && studio.lockButton.Text == "鎖定掃描範圍", "Edit toggle text");
   Check(studio.GeometryFields().All(f => f.Enabled) && studio.centerButton.Enabled, "Edit mode unlocks the coordinate fields");
   var boxes = Descendants(studio).OfType<CheckBox>().ToArray();
   Check(boxes.Length == 1 && boxes[0].Text.Contains("閃爍"), "Only the blink checkbox remains on the toolbar");
   studio.blink.Checked = false; Check(!studio.overlay.Blink && studio.Current().Blink == false, "Blink toggle reaches the overlay and the profile");
   studio.blink.Checked = true; Check(studio.overlay.Blink, "Blink can be switched back on");
   studio.SetScanning(true);
   Check(studio.IsScanning && studio.overlay.Locked && studio.scanButton.Text == "結束掃描 (F8)", "Starting a scan locks the box");
   Check(!studio.lockButton.Enabled, "Scanning forces the lock, so the mode toggle is disabled");
   studio.SetScanning(false);
   Check(!studio.IsScanning && !studio.overlay.Locked && !studio.overlay.Scanning && studio.scanButton.Text == "開始掃描 (F7)", "Stopping a scan returns to edit mode");
   Check(studio.lockButton.Enabled, "Stopping a scan hands the mode toggle back");
   Check(ScanStudioForm.KeyStart == 118 && ScanStudioForm.KeyStop == 119 && ScanStudioForm.HotStart != ScanStudioForm.HotStop, "F7 starts and F8 stops with distinct hotkey ids");
   var saved = studio.Current(); ScanProfile.Validate(saved); Check(saved.Rules.Count == 2 && saved.Interval == ScanOverlay.DefaultInterval, "Studio exports a valid profile");
   Check(studio.Rows().Last().Controls.OfType<Button>().Any(b => b.Text == "移除"), "Each row offers a remove button");
   studio.RemoveRow(studio.Rows().Last());
   Check(studio.Rows().Count() == 1 && studio.overlay.Targets.Count == 1, "Remove drops the row and its colours");
   var panel = studio.Controls[0]; studio.Controls.Remove(panel); panel.Size = studio.ClientSize; panel.CreateControl(); panel.PerformLayout();
   using (var bitmap = new Bitmap(panel.Width, panel.Height)) { panel.DrawToBitmap(bitmap, new Rectangle(Point.Empty, panel.Size)); bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-scan-panel.png")); }
   panel.Dispose();
  }
  // 左右兩格是閃爍的兩個相位，方便比對辨識度。
  using (var bitmap = new Bitmap(860, 300)) using (var g = Graphics.FromImage(bitmap))
  {
   // 左右兩格是閃爍的兩個相位：亮相位畫高亮，暗相位什麼都不畫，所以看到的是目標原色。
   g.Clear(Color.FromArgb(245, 247, 250));
   var sample = Color.FromArgb(214, 64, 64);
   var patch = new Rectangle(90, 70, 150, 110);
   var demo = ColorScanner.Scan(Canvas(420, 300, Color.White, patch, sample), 420, 300, new[] { sample }, new[] { 20 }, 2, 4);
   // 兩格都先畫上被掃描的目標色，代表螢幕上本來就有的東西。
   using (var brush = new SolidBrush(sample)) { g.FillRectangle(brush, patch); g.FillRectangle(brush, patch.X + 440, patch.Y, patch.Width, patch.Height); }
   ScanOverlay.PaintHits(g, Point.Empty, demo.Hits, new[] { ColorRule.Contrast(sample) });
   using (var pen = new Pen(Color.FromArgb(0, 120, 215), 2))
   {
    pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash; g.DrawRectangle(pen, 1, 1, 417, 297); g.DrawRectangle(pen, 441, 1, 417, 297);
   }
   using (var font = new Font("Microsoft JhengHei UI", 9, FontStyle.Bold)) { g.DrawString("亮相位：畫高亮", font, Brushes.DimGray, 8, 6); g.DrawString("暗相位：不畫（看到原色，掃描在此進行）", font, Brushes.DimGray, 448, 6); }
   bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "preview-scan.png"));
  }
 }
}
