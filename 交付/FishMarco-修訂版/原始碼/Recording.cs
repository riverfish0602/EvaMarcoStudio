using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Threading.Tasks;

public sealed class InputRecorder:IDisposable {
 [DllImport("user32.dll",SetLastError=true)] static extern IntPtr SetWindowsHookEx(int id,Hook fn,IntPtr module,uint thread);
 [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hook);
 [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr wp,IntPtr lp);
 [DllImport("kernel32.dll",CharSet=CharSet.Auto)] static extern IntPtr GetModuleHandle(string name);
 delegate IntPtr Hook(int code,IntPtr wp,IntPtr lp);
 [StructLayout(LayoutKind.Sequential)] struct MouseData{public int X,Y;public uint Data,Flags,Time;public UIntPtr Extra;}
 [StructLayout(LayoutKind.Sequential)] struct KeyData{public uint Key,Scan,Flags,Time;public UIntPtr Extra;}
 readonly Hook keyboard,mouse;IntPtr keyboardHook,mouseHook;readonly Stopwatch clock=new Stopwatch();long stoppedAt=-1;public readonly RecordingBuffer Buffer=new RecordingBuffer();
 public string Error{get;private set;}public long Elapsed{get{return clock.ElapsedMilliseconds;}}public bool StopRequested{get;private set;}
 public InputRecorder(){keyboard=OnKey;mouse=OnMouse;}
 public void Start(){if(keyboardHook!=IntPtr.Zero)throw new InvalidOperationException();clock.Restart();var module=GetModuleHandle(null);keyboardHook=SetWindowsHookEx(13,keyboard,module,0);if(keyboardHook==IntPtr.Zero)throw new Exception("無法註冊鍵盤錄製，Windows 錯誤 "+Marshal.GetLastWin32Error());mouseHook=SetWindowsHookEx(14,mouse,module,0);if(mouseHook==IntPtr.Zero){int error=Marshal.GetLastWin32Error();Dispose();throw new Exception("無法註冊滑鼠錄製，Windows 錯誤 "+error);}}
 IntPtr OnKey(int code,IntPtr wp,IntPtr lp){if(code>=0){try{var d=(KeyData)Marshal.PtrToStructure(lp,typeof(KeyData));if((d.Flags&0x10)==0){int msg=wp.ToInt32();bool up=msg==0x101||msg==0x105;if(d.Key==(uint)Keys.F6){if(!up&&!StopRequested){stoppedAt=Elapsed;StopRequested=true;}return new IntPtr(1);}if(!StopRequested&&(msg==0x100||msg==0x104||up))Buffer.Key((int)d.Key,up,Elapsed);}}catch(Exception ex){Error=ex.Message;StopRequested=true;}}return CallNextHookEx(keyboardHook,code,wp,lp);}
 IntPtr OnMouse(int code,IntPtr wp,IntPtr lp){if(code>=0&&!StopRequested){try{var d=(MouseData)Marshal.PtrToStructure(lp,typeof(MouseData));if((d.Flags&1)==0){var p=new Point(d.X,d.Y);long t=Elapsed;switch(wp.ToInt32()){case 0x200:Buffer.Move(p,t);break;case 0x201:Buffer.Mouse("左鍵",false,p,t);break;case 0x202:Buffer.Mouse("左鍵",true,p,t);break;case 0x204:Buffer.Mouse("右鍵",false,p,t);break;case 0x205:Buffer.Mouse("右鍵",true,p,t);break;case 0x207:Buffer.Mouse("中鍵",false,p,t);break;case 0x208:Buffer.Mouse("中鍵",true,p,t);break;case 0x20a:Buffer.Wheel((short)(d.Data>>16),p,t);break;}}}catch(Exception ex){Error=ex.Message;StopRequested=true;}}return CallNextHookEx(mouseHook,code,wp,lp);}
 public List<Step> Finish(){long time=stoppedAt>=0?stoppedAt:Elapsed;Dispose();return Buffer.Finish(time);}
 public void Dispose(){if(keyboardHook!=IntPtr.Zero){UnhookWindowsHookEx(keyboardHook);keyboardHook=IntPtr.Zero;}if(mouseHook!=IntPtr.Zero){UnhookWindowsHookEx(mouseHook);mouseHook=IntPtr.Zero;}clock.Stop();}
}

public partial class MainForm {
 readonly ExtendedActionFields extendedFields=new ExtendedActionFields();InputRecorder recorder;readonly Timer recordTimer=new Timer{Interval=50};bool recordHotkeys;FormWindowState beforeRecord;Button finishRecord; System.Threading.CancellationTokenSource recordingCountdown; RecordingBadge recordingBadge;
 void SetupRecording(FlowLayoutPanel toolbar){AddButton(toolbar,"錄製 F5（倒數 3 秒）",StartRecording);finishRecord=new Button{Text="結束錄製 F6",AutoSize=true,Enabled=false};finishRecord.Click+=(s,e)=>FinishRecording();toolbar.Controls.Add(finishRecord);recordTimer.Tick+=(s,e)=>{if(recorder==null)return;if(recordingBadge!=null)recordingBadge.SetProgress(recorder.Elapsed,recorder.Buffer.Count);status.Text="錄製中｜F6 結束 · "+(recorder.Elapsed/1000.0).ToString("0.0")+" 秒 · "+recorder.Buffer.Count+" 個事件";if(recorder.StopRequested||recorder.Buffer.Full||recorder.Elapsed>=600000)FinishRecording();};Disposed+=(s,e)=>{recordTimer.Dispose();if(recordingCountdown!=null)recordingCountdown.Cancel();if(recorder!=null)recorder.Dispose();if(recordingBadge!=null)recordingBadge.Dispose();};}
 void RegisterRecording(){bool a=Native.RegisterHotKey(Handle,5,0x4000,(uint)Keys.F5),b=Native.RegisterHotKey(Handle,6,0x4000,(uint)Keys.F6);recordHotkeys=a&&b;if(!recordHotkeys){if(a)Native.UnregisterHotKey(Handle,5);if(b)Native.UnregisterHotKey(Handle,6);}if(!recordHotkeys)status.Text+="｜F5／F6 被占用，無法錄製，請關閉占用程式後重開。";}
 async void StartRecording(){
  if(recorder!=null||cancel!=null||capturing||editingDialog||KeyPicker.AnyRecording||newDrag.Dragging||extendedFields.Dragging||!Enabled||Form.ActiveForm!=null&&Form.ActiveForm!=this)return;
  beforeRecord=WindowState;
  try{
   if(!recordHotkeys)throw new Exception("F5／F6 錄製熱鍵不可用，請關閉占用程式後重開。");
   if(scanWindow!=null&&!scanWindow.IsDisposed)throw new Exception("請先關閉顏色掃描小工具，再開始錄製。");
   if(grid.IsCurrentCellInEditMode&&!grid.EndEdit())return;if(grid.Rows.Count>=10000)throw new Exception("目前範本已滿，請先新增範本。");
   capturing=true;SetEditing(false);finishRecord.Enabled=true;ClearMarkers();WindowState=FormWindowState.Minimized;
   recordingCountdown=new System.Threading.CancellationTokenSource();
   try{using(var overlay=new CountdownOverlay("即將錄製 · F6 取消")){
    overlay.Show();await CountdownOverlay.Count(recordingCountdown.Token,n=>{overlay.SetNumber(n);status.Text=n+" 秒後開始錄製 · F6 取消";},(ms,ct)=>Task.Delay(ms,ct));
   }}finally{recordingCountdown.Dispose();recordingCountdown=null;}
   if(IsDisposed)return;
   recorder=new InputRecorder();recorder.Start();recordingBadge=new RecordingBadge();recordingBadge.Show();recordTimer.Start();
  }catch(OperationCanceledException){RestoreRecordingWindow();status.Text="已取消錄製倒數，沒有新增動作。";}
  catch(Exception ex){if(recorder!=null){recorder.Dispose();recorder=null;}RestoreRecordingWindow();if(!IsDisposed)MessageBox.Show(this,ex.Message,"無法錄製");}
 }
 void RestoreRecordingWindow(){recordTimer.Stop();if(recordingBadge!=null){recordingBadge.Dispose();recordingBadge=null;}capturing=false;if(IsDisposed)return;SetEditing(true);finishRecord.Enabled=false;WindowState=beforeRecord;Show();Activate();}
 void FinishRecording(){if(recordingCountdown!=null){recordingCountdown.Cancel();return;}if(recorder==null)return;recordTimer.Stop();var active=recorder;string reason=active.Buffer.Full?"（已達事件上限）":active.Elapsed>=600000?"（已達 10 分鐘上限）":"";recorder=null;var recorded=active.Finish();RestoreRecordingWindow();
  // Keep the complete capture if the existing template leaves too little room.
  if(grid.Rows.Count+recorded.Count>10000){using(var dialog=new SaveFileDialog{Filter="動作範本 (*.json)|*.json",FileName="錄製範本.json"}){while(true){if(dialog.ShowDialog(this)!=DialogResult.OK){if(MessageBox.Show(this,"範本空間不足。放棄這次錄製？","錄製未保存",MessageBoxButtons.YesNo)==DialogResult.Yes)break;continue;}try{JsonFile.Write(dialog.FileName,new Template{Steps=recorded});break;}catch(Exception ex){MessageBox.Show(this,ex.Message);}}}status.Text="錄製完成；目前範本空間不足，已提供另存。";return;}
  rebuilding=true;grid.SuspendLayout();try{foreach(var step in recorded){int i=grid.Rows.Add();SetRow(grid.Rows[i],step);}}finally{grid.ResumeLayout();rebuilding=false;}Renumber();draftVisible=false;RefreshMarkers();status.Text="錄製完成"+reason+"：新增 "+recorded.Count+" 個動作（Ctrl+Z 可復原，Ctrl+S 儲存）"+(active.Error==null?"":"｜錄製中止："+active.Error);
 }
}
