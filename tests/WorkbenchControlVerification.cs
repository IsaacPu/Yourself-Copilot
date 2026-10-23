using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using LocalDesktopNote;
internal static class WorkbenchControlVerification {
 static int count;
 static void Check(bool test,string name) { if(!test) throw new Exception("FAIL: "+name); count++; Console.WriteLine("PASS: "+name); }
 static void Click(ButtonBase button) { button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); }
 static System.Windows.Forms.FormClosingEventArgs SimulateClosing(WorkbenchWindow window,System.Windows.Forms.CloseReason reason) { var args=new System.Windows.Forms.FormClosingEventArgs(reason,false); typeof(System.Windows.Forms.Form).GetMethod("OnFormClosing",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(window,new object[]{args}); return args; }
 static System.Collections.Generic.IEnumerable<T> All<T>(DependencyObject item) where T:DependencyObject { if(item is T) yield return (T)item; for(int i=0;i<VisualTreeHelper.GetChildrenCount(item);i++) foreach(var child in All<T>(VisualTreeHelper.GetChild(item,i))) yield return child; }
 [STAThread] static int Main() {
  Console.OutputEncoding=new System.Text.UTF8Encoding(false);
  string folder=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"workbench-control-"+Guid.NewGuid().ToString("N"));
  WorkbenchWindow window=null;
  try {
   var store=new WorkboardStore(folder); DateTime currentDay=DateTime.Today; DateTime initialDay=currentDay; store.TodayProvider=delegate { return currentDay; };
   var yesterday=store.AddTask("昨天原计划",store.Today.AddDays(-1));
   var today=store.AddTask("今天执行",store.Today); var tomorrow=store.AddTask("明天准备",store.Today.AddDays(1)); var later=store.AddTask("以后再说",null);
   store.State.Ideas="备忘原文\n请整理一份需求"; store.State.TaskDraft="未提交草稿"; store.Save();
   window=new WorkbenchWindow(store);
   Check(window.TaskChecks.Count==2 && window.TaskEditors[0].Text==yesterday.Text,"today includes earlier unfinished tasks in original order");
   Check(window.IdeasEditor.Text==store.State.Ideas && window.NewTaskEditor.Text=="未提交草稿","window reads shared notes and task draft");
   Check(window.DraftDatePicker.SelectedDate==store.Today && window.UnplannedCheck.IsChecked==false,"new draft initially defaults to today");
   window.TaskChecks[0].IsChecked=true; Click(window.TaskChecks[0]);
   Check(yesterday.Done && window.TaskChecks.Count==1 && window.CompletedChecks.Count==1 && !window.CompletedGroup.IsExpanded,"completion leaves pending and enters collapsed completed group");
   window.CompletedChecks[0].IsChecked=false; Click(window.CompletedChecks[0]);
   Check(!yesterday.Done && window.TaskEditors[0].Text=="昨天原计划" && yesterday.PlanDate==WorkboardStore.FormatPlanDate(initialDay.AddDays(-1)),"restore keeps original date and position");
   window.SelectScope("tomorrow"); Check(window.TaskChecks.Count==1 && window.TaskEditors[0].Text==tomorrow.Text,"tomorrow scope uses concrete date");
   window.SelectScope("later"); Check(window.TaskChecks.Count==1 && window.TaskEditors[0].Text==later.Text,"unscheduled tasks appear in later scope");
   window.SelectScope("all"); window.TaskEditors[0].Text="   "; Check(window.FinishMemoHistory() && yesterday.Text=="昨天原计划" && window.TaskEditors[0].Text==yesterday.Text,"blank title preserves last valid content");
   window.NewTaskEditor.Text="添加到明天"; window.DraftDatePicker.SelectedDate=store.Today.AddDays(1); window.AddDraft();
   Check(store.State.Tasks.Last().PlanDate==WorkboardStore.FormatPlanDate(initialDay.AddDays(1)) && window.CurrentScope=="tomorrow" && window.NewTaskEditor.Text=="","adding planned task clears draft and reveals its group");
   window.NewTaskEditor.Text="添加到未安排"; window.UnplannedCheck.IsChecked=true; window.AddDraft();
   Check(store.State.Tasks.Last().PlanDate==null && window.CurrentScope=="later" && store.State.DraftPlanDate==null && window.UnplannedCheck.IsChecked==true,"adding an unscheduled task resets the next draft to later-scope default");
   window.NewTaskEditor.Text="转备忘时保留这个任务草稿"; window.IdeasEditor.Select(5,9);
   int before=store.State.Tasks.Count; string original=window.IdeasEditor.Text;
   Check(window.ConvertMemo(false) && store.State.Tasks.Count==before+1 && window.IdeasEditor.Text==original && window.NewTaskEditor.Text=="转备忘时保留这个任务草稿" && store.State.TaskDraft==window.NewTaskEditor.Text,"memo conversion preserves original notes and task draft");
   Check(!window.ConvertMemo(false) && store.State.Tasks.Count==before+1,"accidental double conversion does not duplicate task");
   store.Remove(today); store.Save(); window.UndoRemove();
   Check(store.State.Tasks[1].Id==today.Id && window.CurrentScope=="today","undo restores source order and reveals date group");
   window.IdeasEditor.Text="保存失败时保留的文字"; Directory.CreateDirectory(store.PathName+".tmp");
   Check(!window.Flush() && window.IdeasEditor.Text.Contains("保存失败") && store.State.Ideas==window.IdeasEditor.Text,"save failure retains current in-memory text");
   Directory.Delete(store.PathName+".tmp"); Check(window.Flush(),"save can retry after storage failure");
   var reopened=new WorkboardStore(folder); Check(reopened.State.TaskDraft==window.NewTaskEditor.Text && reopened.State.Ideas==window.IdeasEditor.Text && reopened.State.DraftPlanDate=="","memo and date/task drafts survive reopening");
   store.State.Ideas="来自桌面卡片的新内容"; store.Save(); Check(window.IdeasEditor.Text==store.State.Ideas,"external store saves synchronize inactive editor");
   Check(Directory.GetFiles(store.HistoryFolder,"*.json",SearchOption.AllDirectories).Length>0,"content edits produce read-only history material");
   Click(All<Button>(window.View).Single(b=>Convert.ToString(b.Content)=="历史记录"));
   var historyPanel=(HistoryPanel)typeof(WorkbenchWindow).GetField("historyPanel",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(window);
   Check(historyPanel.CurrentCategory=="memo" && historyPanel.CurrentFilter=="archive" && window.HistoryList.Items.Count==0,"history defaults to deliberately stored papers rather than automatic recovery versions");
   historyPanel.SelectCategory("memo","versions");
   Check(window.HistoryList.Items.Count>0 && window.HistoryPreview.IsReadOnly && historyPanel.Entries.Any(e=>e.Text==window.HistoryPreview.Text) && !window.HistoryPreview.Text.Contains("\"Version\":"),"history presents human-readable read-only content");
   var expectedMemoVersions=Directory.GetFiles(store.HistoryFolder,"*.json",SearchOption.AllDirectories).Select(path=>store.ReadHistorySnapshot(path).Ideas).Where(text=>!String.IsNullOrWhiteSpace(text)).Distinct().ToArray();
   foreach(string version in expectedMemoVersions) {
    int versionIndex=historyPanel.Entries.FindIndex(e=>e.Text==version); window.HistoryList.SelectedIndex=versionIndex;
    Check(versionIndex>=0 && window.HistoryPreview.IsReadOnly && window.HistoryPreview.Text==version,"every automatic memo recovery version is selectable as exact read-only original text");
   }
   historyPanel.SelectCategory("task","versions");
   Check(window.HistoryList.Items.Count>0 && window.HistoryPreview.IsReadOnly && window.HistoryPreview.Text.Contains("任务") && !window.HistoryPreview.Text.Contains("来自桌面卡片的新内容") && !window.HistoryPreview.Text.Contains("保存失败时保留的文字") && historyPanel.RestoreButton.Visibility==Visibility.Collapsed,"task history remains readable and read-only without unrelated memo content or overwrite action");
   bool autoStart=false; window.GetAutoStart=delegate { return autoStart; }; window.SetAutoStart=delegate(bool value) { autoStart=value; };
   Click(All<Button>(window.View).Single(b=>Convert.ToString(b.Content)=="设置"));
   Check(window.LaunchAtLoginCheck.IsChecked==false && window.StartupCardCheck.IsChecked==true,"startup and card visibility settings have separate true state");
   window.LaunchAtLoginCheck.IsChecked=true; Click(window.LaunchAtLoginCheck);
   Check(autoStart && window.LaunchAtLoginCheck.IsChecked==true,"auto-start toggle verifies callback result");
   window.SetAutoStart=delegate(bool value) { throw new IOException("拒绝写入测试"); }; window.LaunchAtLoginCheck.IsChecked=false; Click(window.LaunchAtLoginCheck);
   Check(window.LaunchAtLoginCheck.IsChecked==true,"failed auto-start change rolls back checkbox to actual state");
   window.StartupCardCheck.IsChecked=false; Click(window.StartupCardCheck);
   Check(!new WorkboardStore(folder).State.ShowCardOnStartup && autoStart,"card startup display persists independently of auto-start");
   window.NewTaskEditor.Clear(); window.SelectScope("tomorrow");
   Check(window.DraftDatePicker.SelectedDate==store.Today.AddDays(1) && store.State.DraftPlanDate==null && window.UnplannedCheck.IsChecked==false,"empty draft adopts a relative tomorrow default when switching scope");
   window.NewTaskEditor.Text="明天页直接添加"; window.SelectScope("today");
   Check(window.DraftDatePicker.SelectedDate==store.Today.AddDays(1) && window.NewTaskEditor.Text=="明天页直接添加","switching scope retains a nonempty draft and its explicit date");
   window.AddDraft(); Check(store.State.Tasks.Last().PlanDate==WorkboardStore.FormatPlanDate(initialDay.AddDays(1)) && window.CurrentScope=="tomorrow","task added from tomorrow default remains planned for tomorrow");
   window.SelectScope("later"); Check(window.UnplannedCheck.IsChecked==true && store.State.DraftPlanDate==null,"empty draft switches to relative unscheduled default in later scope");
   window.SelectScope("today"); Check(window.DraftDatePicker.SelectedDate==store.Today && window.UnplannedCheck.IsChecked==false,"empty draft switches back to today default");
   store.State.DraftPlanDate=null; store.Save(); string notesBefore=window.IdeasEditor.Text;
   currentDay=currentDay.AddDays(1); window.RefreshDay();
   Check(window.DraftDatePicker.SelectedDate==store.Today && window.NewTaskEditor.Text=="" && window.IdeasEditor.Text==notesBefore && window.TaskEditors.Any(t=>((WorkTask)t.Tag).Id==tomorrow.Id),"date rollover refreshes grouping and blank default date without clearing notes");
   var sameEditor=window.TaskEditors[0]; window.RefreshDay();
   Check(Object.ReferenceEquals(sameEditor,window.TaskEditors[0]),"same-day clock ticks do not rebuild task editors");
   window.DraftDatePicker.SelectedDate=store.Today.AddDays(4); DateTime explicitDate=window.DraftDatePicker.SelectedDate.Value;
   window.NewTaskEditor.Text="跨天保留的草稿"; currentDay=currentDay.AddDays(1); window.RefreshDay();
   Check(window.DraftDatePicker.SelectedDate==explicitDate && window.NewTaskEditor.Text=="跨天保留的草稿" && window.IdeasEditor.Text==notesBefore,"rollover retains explicit date and typed task/memo content");
   window.UnplannedCheck.IsChecked=true; currentDay=currentDay.AddDays(1); window.RefreshDay();
   Check(window.UnplannedCheck.IsChecked==true && store.State.DraftPlanDate=="" && window.NewTaskEditor.Text=="跨天保留的草稿","rollover retains explicit unscheduled draft");
   window.NewTaskEditor.Clear(); window.SelectScope("tomorrow"); window.NewTaskEditor.Text="明天页任务A"; window.AddDraft();
   Check(window.NewTaskEditor.Text=="" && store.State.DraftPlanDate==null && window.CurrentScope=="tomorrow","successful add resets blank next draft to relative scope default");
   currentDay=currentDay.AddDays(1); window.RefreshDay();
   Check(window.DraftDatePicker.SelectedDate==store.Today.AddDays(1) && store.State.DraftPlanDate==null,"blank tomorrow draft rolls forward to new tomorrow after midnight");
   window.NewTaskEditor.Text="明天页任务B"; window.AddDraft();
   Check(store.State.Tasks.Last().PlanDate==WorkboardStore.FormatPlanDate(store.Today.AddDays(1)) && window.CurrentScope=="tomorrow","consecutive add across midnight assigns B to new tomorrow rather than today");
   Check(SimulateClosing(window,System.Windows.Forms.CloseReason.UserClosing).Cancel && !window.Visible,"user close hides workbench and cancels disposal");
   Check(!SimulateClosing(window,System.Windows.Forms.CloseReason.WindowsShutDown).Cancel,"successful flush allows simulated Windows shutdown or logout");
   Check(!SimulateClosing(window,System.Windows.Forms.CloseReason.ApplicationExitCall).Cancel,"successful flush allows application exit");
   window.IdeasEditor.Text+="\n关机保存失败仍保留"; Directory.CreateDirectory(store.PathName+".tmp");
   Check(SimulateClosing(window,System.Windows.Forms.CloseReason.WindowsShutDown).Cancel && window.IdeasEditor.Text.Contains("关机保存失败仍保留"),"failed shutdown flush cancels closing and preserves current content");
   Directory.Delete(store.PathName+".tmp"); Check(window.Flush(),"failed closing save can be retried after storage recovers");
   window.AllowClose=true; window.Close(); window.Dispose(); window=null;
   Console.WriteLine("TOTAL: "+count); return 0;
  } catch(Exception error) { Console.WriteLine(error); return 1; }
  finally { if(window!=null) window.Dispose(); }
 }
}
