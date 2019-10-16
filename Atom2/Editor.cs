using System;
using System.Collections.Generic;
using System.Threading;
using Eto.Drawing;
using Eto.Forms;

namespace Atom2
{
  public sealed class Editor : Form
  {
    private static readonly Application Application = new Application();
    private static readonly Font StandardFont = new Font("Helvetica", 12);
    private readonly TextArea codeTextArea;
    private readonly ListBox callStackListBox;
    private readonly TreeGridView codeTreeGridView;
    private readonly ManualResetEvent manualResetEvent = new ManualResetEvent(false);
    private readonly TextArea outputTextArea;
    private readonly Runtime runtime;
    private readonly ListBox stackListBox;
    private readonly UITimer timer = new UITimer();
    private bool running;
    private bool paused;
    private bool stepMode;
    private readonly Command runCommand;
    private readonly Command continueCommand;
    private readonly CheckMenuItem checkMenuItem;

    private Editor(params string[] arguments)
    {
      // Runtime:
      runtime = new Runtime(Application, arguments[0]);
      runtime.Breaking += OnBreaking;
      runtime.Stepping += OnStepping;

      // Menu:
      Title = "Atom2";
      WindowState = WindowState.Maximized;
      runCommand = new Command(OnRun);
      runCommand.MenuText = "&Run";
      continueCommand = new Command(OnContinue);
      continueCommand.MenuText = "&Continue";
      continueCommand.Shortcut = Keys.F10;
      checkMenuItem = new CheckMenuItem(new CheckCommand(OnStep));
      checkMenuItem.Text = "&Step";
      ButtonMenuItem fileMenuItem = new ButtonMenuItem();
      fileMenuItem.Text = "&File";
      fileMenuItem.Items.Add(runCommand);
      fileMenuItem.Items.Add(continueCommand);
      fileMenuItem.Items.Add(checkMenuItem);
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
      stackListBox.Width = 250;

      // Right column:
      callStackListBox = new ListBox();
      callStackListBox.Height = 250;
      callStackListBox.SelectedIndexChanged += OnCallStackListBoxSelectedIndexChanged;
      codeTreeGridView = NewTreeGridView("Value", "Type");
      codeTreeGridView.Width = 250;

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

    private void OnCallStackListBoxSelectedIndexChanged(object sender, EventArgs e)
    {
      ListItem selectedItem = (ListItem) callStackListBox.Items[callStackListBox.SelectedIndex];
      CallEnvironment callEnvironment = (CallEnvironment) selectedItem.Tag;
      RebuildCodeTreeView(callEnvironment.Items, callEnvironment.CurrentItem);
    }

    private void OnStepping()
    {
      if (stepMode)
      {
        Pause();
      }
    }

    private void OnElapsed(object sender, EventArgs e)
    {
      runCommand.Enabled = !running;
      continueCommand.Enabled = paused;
      checkMenuItem.Checked = stepMode;
    }

    private void OnStep(object sender, EventArgs e)
    {
      stepMode = !stepMode;
    }

    public static void Run(string[] arguments)
    {
      Application.Run(new Editor(arguments));
    }

    private static TreeGridView NewTreeGridView(params string[] headers)
    {
      TreeGridView result = new TreeGridView();
      for (int i = 0; i < headers.Length; ++i)
      {
        GridColumn currentGridColumn = new GridColumn();
        currentGridColumn.HeaderText = headers[i];
        currentGridColumn.Editable = true;
        currentGridColumn.DataCell = new TextBoxCell(i);
        result.Columns.Add(currentGridColumn);
      }
      result.Width = 300;
      return result;
    }

    private static TreeGridItemCollection RebuildTrackWindow(IEnumerable<object> rootItems, object executingItem, ref TreeGridItem executingTreeGridViewItem, int indentation = 0)
    {
      TreeGridItemCollection result = new TreeGridItemCollection();
      string indentationPrefix = new string(' ', indentation * 2);
      foreach (object currentItem in rootItems)
      {
        TreeGridItem newTreeViewItem = currentItem is Items currentItems ? new TreeGridItem(RebuildTrackWindow(currentItems, executingItem, ref executingTreeGridViewItem, indentation + 1), "(Block)") : new TreeGridItem(indentationPrefix + currentItem);
        newTreeViewItem.Expanded = true;
        result.Add(newTreeViewItem);
        if (currentItem == executingItem)
        {
          executingTreeGridViewItem = newTreeViewItem;
        }
      }
      return result;
    }

    private void UpdatePauseUI()
    {
      CallEnvironments callEnvironments = runtime.CallEnvironments;
      CallEnvironment topmostCallEnvironment = callEnvironments.Peek();
      RebuildCallStackListBox(callEnvironments);
      RebuildCodeTreeView(topmostCallEnvironment.Items, topmostCallEnvironment.CurrentItem);
      RebuildStackListBox();
    }

    private void RebuildStackListBox()
    {
      stackListBox.Items.Clear();
      foreach (var currentValue in runtime.Stack)
      {
        stackListBox.Items.Add(currentValue.ToString());
      }
    }

    private void RebuildCallStackListBox(CallEnvironments callEnvironments)
    {
      callStackListBox.Items.Clear();
      foreach (CallEnvironment currentCallEnvironment in callEnvironments)
      {
        ListItem newListItem = new ListItem();
        newListItem.Text = currentCallEnvironment.CurrentItem.ToString();
        newListItem.Tag = currentCallEnvironment;
        callStackListBox.Items.Add(newListItem);
      }
    }

    private void DoRun(object code)
    {
      runtime.Run((string) code, out _);
    }

    private void OnBreaking()
    {
      Pause();
    }

    private void Pause()
    {
      paused = true;
      Application.Invoke(UpdatePauseUI);
      manualResetEvent.WaitOne();
      manualResetEvent.Reset();
      paused = false;
    }

    private void OnContinue(object sender, EventArgs e)
    {
      manualResetEvent.Set();
    }

    private void OnRun(object sender, EventArgs arguments)
    {
      running = true;
      Thread thread = new Thread(DoRun);
      thread.Start(codeTextArea.Text);
    }

    private void RebuildCodeTreeView(Items items, object executingItem)
    {
      TreeGridItem executingTreeGridItem = null;
      codeTreeGridView.DataStore = RebuildTrackWindow(items, executingItem, ref executingTreeGridItem);
      codeTreeGridView.SelectedItem = executingTreeGridItem;
    }
  }
}