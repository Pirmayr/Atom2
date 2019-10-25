using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Eto.Drawing;
using Eto.Forms;

namespace Atom2
{
  public sealed class Editor : Form
  {
    private const int StandardWidth = 300;
    private static readonly Application Application = new Application();
    private static readonly Font StandardFont = new Font("Helvetica", 12);
    private readonly ListBox callStackListBox;
    private readonly TextArea codeTextArea;
    private readonly TreeGridView codeTreeGridView;
    private readonly Command continueCommand;
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
      // Runtime:
      runtime = new Runtime(Application, arguments[0]);
      runtime.Breaking += OnBreaking;
      runtime.Stepping += OnStepping;
      runtime.Outputting += OnOutput;

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

      // Code:
      codeTextArea = new TextArea();
      codeTextArea.Font = StandardFont;
      codeTextArea.Text = Runtime.Code(arguments[1]);
      TableCell codeTableCell = new TableCell(codeTextArea, true);
      TableRow codeTableRow = new TableRow(codeTableCell);
      codeTableRow.ScaleHeight = true;

      // Output:
      outputTextArea = new TextArea();
      TableRow outputTableRow = new TableRow(outputTextArea);
      outputTextArea.Height = 250;

      // Central column:
      TableLayout centerTableLayout = new TableLayout(codeTableRow, outputTableRow);
      TableCell centerTableCell = new TableCell(centerTableLayout, true);

      // Left column:
      stackListBox = new ListBox();
      stackListBox.Width = StandardWidth;

      // Right column:
      callStackListBox = new ListBox();
      callStackListBox.Height = 250;
      callStackListBox.SelectedIndexChanged += OnCallStackListBoxSelectedIndexChanged;
      codeTreeGridView = NewTreeGridView();
      codeTreeGridView.ShowHeader = false;
      codeTreeGridView.Width = StandardWidth;
      TableLayout rightColumnLayout = new TableLayout(callStackListBox, codeTreeGridView);

      // Table row:
      TableRow tableRow = new TableRow(stackListBox, centerTableCell, rightColumnLayout);

      // Layout
      TableLayout layout = new TableLayout();
      layout.Rows.Add(tableRow);
      Content = layout;

      // Other initializations:
      runtime = new Runtime(Application, arguments[0]);
      runtime.Breaking += OnBreaking;
      runtime.Stepping += OnStepping;
      timer.Interval = 0.3;
      timer.Elapsed += OnElapsed;
      timer.Start();
    }

    private void OnOutput(object sender, string message)
    {
      outputTextArea.Append(message + Environment.NewLine);
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
      GridColumn currentGridColumn = new GridColumn();
      currentGridColumn.Editable = true;
      currentGridColumn.DataCell = new TextBoxCell(0);
      result.Columns.Add(currentGridColumn);
      result.Width = 300;
      return result;
    }

    private Exception DoRun(object code)
    {
      return runtime.Run((string) code, true);
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

    private async void OnRun(object sender, EventArgs arguments)
    {
      running = true;
      Exception exception = await Task<Exception>.Factory.StartNew(DoRun, codeTextArea.Text);
      running = false;
      if (exception != null)
      {
        outputTextArea.Append(exception.Message + Environment.NewLine);
        UpdatePauseUI();
      }
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
        newListItem.Text = currentCallEnvironment.CurrentItem == null? "(null)" : currentCallEnvironment.CurrentItem.ToInformation();
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
      CallEnvironment topmostCallEnvironment = callEnvironments.Peek();
      RebuildCallStackListBox(callEnvironments);
      RebuildCodeTreeView(topmostCallEnvironment.Items, topmostCallEnvironment.CurrentItem);
      RebuildStackListBox();
    }
  }
}