using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Eto.Forms;

namespace Atom2
{
  public sealed class Editor : Form
  {
    private const int StandardDimension = 300;
    private static readonly Application Application = new Application();
    private readonly ListBox callStackListBox;
    private readonly TreeGridView codeTreeGridView;
    private readonly Command continueCommand;
    private readonly string currentCode;
    private readonly ManualResetEvent manualResetEvent = new ManualResetEvent(false);
    private readonly TextArea outputTextArea;
    private readonly Command runCommand;
    private readonly Runtime runtime;
    private readonly ListBox stackListBox;
    private readonly Command stepCommand;
    private readonly UITimer timer = new UITimer();
    private bool paused;
    private bool running;
    private bool stepMode;

    private Editor(params string[] arguments)
    {
      runtime = new Runtime(Application, arguments[0]);
      currentCode = runtime.Code(arguments[1]);

      // Menu:
      Title = "Atom2";
      WindowState = WindowState.Maximized;
      runCommand = new Command(OnRun);
      runCommand.MenuText = "&Run";
      continueCommand = new Command(OnContinue);
      continueCommand.MenuText = "&Continue";
      stepCommand = new Command(OnStep);
      stepCommand.MenuText = "&Step";
      stepCommand.Shortcut = Keys.F10;
      ButtonMenuItem fileMenuItem = new ButtonMenuItem();
      fileMenuItem.Text = "&File";
      fileMenuItem.Items.Add(runCommand);
      fileMenuItem.Items.Add(continueCommand);
      fileMenuItem.Items.Add(stepCommand);
      MenuBar menuBar = new MenuBar();
      menuBar.Items.Add(fileMenuItem);
      Menu = menuBar;
      codeTreeGridView = NewTreeGridView();
      callStackListBox = new ListBox {Width = StandardDimension};
      stackListBox = new ListBox {Width = StandardDimension};
      outputTextArea = new TextArea {Height = StandardDimension};
      TableLayout layout = new TableLayout();
      layout.Rows.Add(new TableRow(stackListBox, new TableCell(new TableLayout(new TableRow(codeTreeGridView) {ScaleHeight = true}, new TableRow(outputTextArea)), true), callStackListBox));
      Content = layout;
      callStackListBox.SelectedIndexChanged += OnCallStackListBoxSelectedIndexChanged;

      // Other initializations:
      runtime.Breaking += OnBreaking;
      runtime.Outputting += OnOutputting;
      runtime.Stepping += OnStepping;
      runtime.Terminating += OnTerminating;
      timer.Interval = 0.3;
      timer.Elapsed += OnElapsed;
      timer.Start();
      runtime.Run(currentCode, false, true);
      UpdatePauseUI();
    }

    public static void Run(string[] arguments)
    {
      Application.Run(new Editor(arguments));
    }

    private static TreeGridItemCollection GetCodeTree(IEnumerable<object> rootItems, object executingItem, ref TreeGridItem executingTreeGridViewItem)
    {
      TreeGridItemCollection result = new TreeGridItemCollection();
      foreach (object currentItem in rootItems)
      {
        TreeGridItem newTreeViewItem = currentItem is Items currentItems ? new TreeGridItem(GetCodeTree(currentItems, executingItem, ref executingTreeGridViewItem), "(Items)") : new TreeGridItem(currentItem.ToInformation());
        newTreeViewItem.Expanded = true;
        result.Add(newTreeViewItem);
        if (currentItem == executingItem)
        {
          executingTreeGridViewItem = newTreeViewItem;
        }
      }
      return result;
    }

    private static TreeGridView NewTreeGridView()
    {
      TreeGridView result = new TreeGridView();
      result.ShowHeader = true;
      GridColumn currentGridColumn = new GridColumn();
      currentGridColumn.Editable = false;
      currentGridColumn.DataCell = new TextBoxCell(0);
      currentGridColumn.Resizable = false;
      currentGridColumn.HeaderText = "Code";
      result.Columns.Add(currentGridColumn);
      return result;
    }

    private Exception DoRun(object code)
    {
      return runtime.Run((string) code, true, true);
    }

    private void OnBreaking()
    {
      Pause();
    }

    private void OnCallStackListBoxSelectedIndexChanged(object sender, EventArgs e)
    {
      ListItem selectedItem = (ListItem) callStackListBox.Items[callStackListBox.SelectedIndex];
      CallEnvironment callEnvironment = (CallEnvironment) selectedItem.Tag;
      RebuildCodeTreeView(callEnvironment.Items, callEnvironment.CurrentItem);
    }

    private void OnContinue(object sender, EventArgs e)
    {
      manualResetEvent.Set();
    }

    private void OnElapsed(object sender, EventArgs e)
    {
      runCommand.Enabled = !running;
      continueCommand.Enabled = paused;
      stepCommand.Enabled = paused;
    }

    private void OnOutputting(object sender, string message)
    {
      outputTextArea.Append(message);
    }

    private void OnRun(object sender, EventArgs arguments)
    {
      running = true;
      Task<Exception>.Factory.StartNew(DoRun, currentCode);
    }

    private void OnStep(object sender, EventArgs e)
    {
      stepMode = true;
      manualResetEvent.Set();
    }

    private void OnStepping()
    {
      if (stepMode)
      {
        stepMode = false;
        Pause();
      }
    }

    private void OnTerminating(object sender, Exception exception)
    {
      running = false;
      if (exception != null)
      {
        outputTextArea.Append(exception.Message + Environment.NewLine);
      }
      UpdatePauseUI();
    }

    private void Pause()
    {
      paused = true;
      Application.Invoke(UpdatePauseUI);
      manualResetEvent.WaitOne();
      manualResetEvent.Reset();
      paused = false;
    }

    private void RebuildCallStackListBox(CallEnvironments callEnvironments)
    {
      callStackListBox.Items.Clear();
      foreach (CallEnvironment currentCallEnvironment in callEnvironments)
      {
        ListItem newListItem = new ListItem();
        newListItem.Text = currentCallEnvironment.CurrentItem == null ? "(null)" : currentCallEnvironment.CurrentItem.ToString();
        newListItem.Tag = currentCallEnvironment;
        callStackListBox.Items.Add(newListItem);
      }
    }

    private void RebuildCodeTreeView(Items items, object executingItem)
    {
      TreeGridItem executingTreeGridItem = null;
      codeTreeGridView.DataStore = GetCodeTree(items, executingItem, ref executingTreeGridItem);
      codeTreeGridView.SelectedItem = executingTreeGridItem;
    }

    private void RebuildStackListBox()
    {
      stackListBox.Items.Clear();
      foreach (object currentValue in runtime.Stack)
      {
        stackListBox.Items.Add(currentValue.GetType().Name);
      }
    }

    private void UpdatePauseUI()
    {
      CallEnvironments callEnvironments = runtime.CallEnvironments;
      RebuildCallStackListBox(callEnvironments);
      CallEnvironment topmostCallEnvironment = callEnvironments.Count == 0 ? null : callEnvironments.Peek();
      Items topMostItems = topmostCallEnvironment == null ? runtime.CurrentRootItems : topmostCallEnvironment.Items;
      object topMostCurrentItem = topmostCallEnvironment?.CurrentItem;
      RebuildCodeTreeView(topMostItems, topMostCurrentItem);
      RebuildStackListBox();
    }
  }
}