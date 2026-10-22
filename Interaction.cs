using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

public class ActionGrid:DataGridView {
 public event Action<int[],int> ReorderRequested;public event Action<int,int> CellEditRequested;
 protected override void OnMouseDoubleClick(MouseEventArgs e){pending=false;base.OnMouseDoubleClick(e);if(e.Button==MouseButtons.Left&&CellEditRequested!=null){var hit=HitTest(e.X,e.Y);CellEditRequested(hit.RowIndex,hit.ColumnIndex);}}
 Point origin;bool pending;int insertion=-1;
 class DragRows {public ActionGrid Owner;public int[] Rows;}
 public ActionGrid(){DoubleBuffered=true;AllowDrop=true;MultiSelect=true;}
 protected override void OnMouseDown(MouseEventArgs e){
  if(IsCurrentCellInEditMode){base.OnMouseDown(e);pending=false;return;}var hit=HitTest(e.X,e.Y);if(e.Button==MouseButtons.Right&&hit.RowIndex>=0&&Rows[hit.RowIndex].Selected){pending=false;return;}bool preserve=e.Button==MouseButtons.Left&&hit.RowIndex>=0&&Rows[hit.RowIndex].Selected&&(ModifierKeys&Keys.Control)==0&&((ModifierKeys&Keys.Shift)==0||SelectedRows.Count>1);
  if(!preserve)base.OnMouseDown(e);pending=e.Button==MouseButtons.Left&&hit.RowIndex>=0;origin=e.Location;
 }
 protected override void OnMouseUp(MouseEventArgs e){pending=false;base.OnMouseUp(e);}
 protected override void OnMouseMove(MouseEventArgs e){
  if(pending&&e.Button==MouseButtons.Left){var threshold=new Rectangle(origin.X-SystemInformation.DragSize.Width/2,origin.Y-SystemInformation.DragSize.Height/2,SystemInformation.DragSize.Width,SystemInformation.DragSize.Height);
   if(!threshold.Contains(e.Location)){pending=false;var rows=SelectedRows.Cast<DataGridViewRow>().Select(r=>r.Index).OrderBy(i=>i).ToArray();try{if(rows.Length>0)DoDragDrop(new DragRows{Owner=this,Rows=rows},DragDropEffects.Move);}finally{insertion=-1;Invalidate();}}return;
  }base.OnMouseMove(e);
 }
 protected override void OnDragOver(DragEventArgs e){
  var data=e.Data.GetData(typeof(DragRows)) as DragRows;if(data==null||data.Owner!=this||!Enabled){e.Effect=DragDropEffects.None;return;}
  e.Effect=DragDropEffects.Move;var p=PointToClient(new Point(e.X,e.Y));var hit=HitTest(p.X,p.Y);
  if(hit.RowIndex>=0){var rect=GetRowDisplayRectangle(hit.RowIndex,false);insertion=hit.RowIndex+(p.Y>=rect.Top+rect.Height/2?1:0);}else insertion=p.Y<ColumnHeadersHeight?0:Rows.Count;
  if(Rows.Count>0&&FirstDisplayedScrollingRowIndex>=0){if(p.Y<ColumnHeadersHeight+20&&FirstDisplayedScrollingRowIndex>0)FirstDisplayedScrollingRowIndex--;else if(p.Y>ClientSize.Height-24&&FirstDisplayedScrollingRowIndex<Rows.Count-1)FirstDisplayedScrollingRowIndex++;}
  Invalidate();base.OnDragOver(e);
 }
 protected override void OnDragLeave(EventArgs e){insertion=-1;Invalidate();base.OnDragLeave(e);}
 protected override void OnDragDrop(DragEventArgs e){var data=e.Data.GetData(typeof(DragRows)) as DragRows;int target=insertion;insertion=-1;if(data!=null&&data.Owner==this&&target>=0&&ReorderRequested!=null)ReorderRequested(data.Rows,target);Invalidate();base.OnDragDrop(e);}
 protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(insertion<0||Rows.Count==0)return;int index=Math.Min(insertion,Rows.Count-1);var rect=GetRowDisplayRectangle(index,false);int y=insertion==Rows.Count?rect.Bottom-1:rect.Top;using(var pen=new Pen(Color.DodgerBlue,4))e.Graphics.DrawLine(pen,0,y,ClientSize.Width,y);}
 public static List<T> Reorder<T>(IList<T> items,int[] selected,int boundary){
  if(boundary<0||boundary>items.Count||selected.Any(i=>i<0||i>=items.Count))throw new ArgumentOutOfRangeException();
  var indexes=new HashSet<int>(selected);var moved=items.Where((item,i)=>indexes.Contains(i)).ToList();var rest=items.Where((item,i)=>!indexes.Contains(i)).ToList();int target=boundary-indexes.Count(i=>i<boundary);rest.InsertRange(target,moved);return rest;
 }
}
public class CountdownOverlay:OverlayForm {
 int number=3;
 public CountdownOverlay(){Size=new Size(260,260);var screen=Screen.PrimaryScreen.Bounds;Location=new Point(screen.Left+(screen.Width-Width)/2,screen.Top+(screen.Height-Height)/2);BackColor=Color.FromArgb(20,29,45);Opacity=.94;}
 public void SetNumber(int value){number=value;Invalidate();Update();}
 protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);using(var font=new Font("Segoe UI",88,FontStyle.Bold))using(var small=new Font("Microsoft JhengHei UI",12))using(var center=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center}){e.Graphics.DrawString(number.ToString(),font,Brushes.White,new RectangleF(0,12,Width,180),center);e.Graphics.DrawString("即將開始 · F10 停止",small,Brushes.LightSkyBlue,new RectangleF(0,200,Width,40),center);}}
 public static async Task Count(CancellationToken token,Action<int> display,Func<int,CancellationToken,Task> wait){for(int n=3;n>=1;n--){token.ThrowIfCancellationRequested();display(n);await wait(1000,token);}token.ThrowIfCancellationRequested();}
 public static async Task Run(CancellationToken token,Action<string> report){using(var overlay=new CountdownOverlay()){overlay.Show();await Count(token,n=>{overlay.SetNumber(n);report(n+" 秒後開始，請切換至目標視窗。F10 停止");},(ms,ct)=>Task.Delay(ms,ct));}}
}

public class BufferedEditorPanel:TableLayoutPanel {
 public BufferedEditorPanel(){DoubleBuffered=true;SetStyle(ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);}

}