namespace Atom2
{
  using System;
  using System.Collections.Generic;
  using System.Threading;
  using System.Threading.Tasks;

  using Eto.Forms;

  public sealed class Editor : Form
  {
    private const int StandardDimension = 300;
    private const string RunText = "Run";
    private const string ContinueText = "Continue";
    private const string StepText = "Step";
    private const string FileText = "File";
    private const string TitleText = "Atom 2";
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
    private readonly Button runButton;
    private readonly Button continueButton;
    private readonly Button stepButton;
    private readonly UITimer timer = new UITimer();
    private bool paused;
    private bool running;
    private bool stepMode;
    private bool firstElapsed = true;

    private Editor(params string[] arguments)
    {
      if (arguments.Length != 2)
      {
        arguments = new string[2];
        arguments[0] = "/Users/pic/Projects/Atom2/Atom2/System";
        arguments[1] = "Program.txt";
      }

      runtime = new Runtime(Application, arguments[0]);
      currentCode = runtime.Code(arguments[1]);

      // Menu:
      Title = TitleText;
      WindowState = WindowState.Maximized;
      runCommand = new Command(OnRun);
      runCommand.MenuText = RunText;
      continueCommand = new Command(OnContinue);
      continueCommand.MenuText = ContinueText;
      stepCommand = new Command(OnStep);
      stepCommand.MenuText = StepText;
      stepCommand.Shortcut = Keys.F10;
      ButtonMenuItem fileMenuItem = new ButtonMenuItem();
      fileMenuItem.Text = FileText;
      fileMenuItem.Items.Add(runCommand);
      fileMenuItem.Items.Add(continueCommand);
      fileMenuItem.Items.Add(stepCommand);
      MenuBar menuBar = new MenuBar();
      menuBar.IncludeSystemItems = MenuBarSystemItems.Quit;
      menuBar.Items.Add(fileMenuItem);
      Menu = menuBar;
      runButton = new Button() { Command = runCommand, Text = RunText  };
      continueButton = new Button() { Command = continueCommand, Text = ContinueText };
      stepButton = new Button() { Command = stepCommand, Text = StepText };
      stackListBox = new ListBox { Width = StandardDimension };
      callStackListBox = new ListBox { Width = StandardDimension };
      codeTreeGridView = NewTreeGridView();
      outputTextArea = new TextArea { Height = StandardDimension };
      TableLayout buttonLayout = TableLayout.Horizontal(runButton, continueButton, stepButton, new Panel());
      TableRow codeTableRow = new TableRow(codeTreeGridView) { ScaleHeight = true };
      TableCell middleCell = new TableCell(new TableLayout(buttonLayout, codeTableRow, outputTextArea)) { ScaleWidth = true };
      Content = new TableRow(stackListBox, middleCell, callStackListBox);
      callStackListBox.SelectedIndexChanged += OnCallStackListBoxSelectedIndexChanged;

      // Other initializations:
      runtime.Breaking += OnBreaking;
      runtime.Outputting += OnOutputting;
      runtime.Stepping += OnStepping;
      runtime.Terminating += OnTerminating;
      timer.Interval = 0;
      timer.Elapsed += OnElapsed;
      timer.Start();
      runtime.Run(currentCode, false, true);
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
        TreeGridItem newTreeViewItem = currentItem is Items currentItems ? new TreeGridItem(GetCodeTree(currentItems, executingItem, ref executingTreeGridViewItem), "(Items)") : new TreeGridItem(currentItem.ToString(), currentItem.GetType().Name);
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
      result.ShowHeader = false;
      GridColumn codeGridColumn = new GridColumn();
      codeGridColumn.Editable = false;
      codeGridColumn.DataCell = new TextBoxCell(0);
      codeGridColumn.Resizable = false;
      codeGridColumn.HeaderText = "Code";
      result.Columns.Add(codeGridColumn);
      GridColumn typeGridColumn = new GridColumn();
      typeGridColumn.Editable = false;
      typeGridColumn.DataCell = new TextBoxCell(1);
      typeGridColumn.Resizable = false;
      typeGridColumn.HeaderText = "Type";
      result.Columns.Add(typeGridColumn);
      return result;
    }

    private Exception DoRun(object code)
    {
      return runtime.Run((string)code, true, true);
    }

    private void OnBreaking()
    {
      Pause();
    }

    private void OnCallStackListBoxSelectedIndexChanged(object sender, EventArgs e)
    {
      int index = callStackListBox.SelectedIndex;
      if (0 <= index && index < callStackListBox.Items.Count)
      {
        ListItem selectedItem = (ListItem)callStackListBox.Items[index];
        CallEnvironment callEnvironment = (CallEnvironment)selectedItem.Tag;
        RebuildCodeTreeView(callEnvironment.Items, callEnvironment.CurrentItem);
      }
    }

    private void OnContinue(object sender, EventArgs e)
    {
      callStackListBox.Items.Clear();
      manualResetEvent.Set();
    }

    private void OnElapsed(object sender, EventArgs e)
    {
      runCommand.Enabled = !running;
      continueCommand.Enabled = paused;
      stepCommand.Enabled = paused;

      if (firstElapsed)
      {
        firstElapsed = false;
        timer.Interval = 0.25;
        UpdatePauseUI();
      }
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
      callStackListBox.Items.Clear();
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