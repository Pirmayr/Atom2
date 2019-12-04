namespace Mira
{
  using System;
  using System.Collections.Generic;

  using Eto.Forms;

  public sealed class Editor : Form
  {
    private const string ContinueText = "Continue";
    private const string FileText = "File";
    private const string HeaderTextCode = "Code";
    private const string HeaderTextType = "Type";
    private const string RunText = "Run";
    private const int StandardDimension = 300;
    private const string StepText = "Step";
    private const string TitleText = "Mira";
    private readonly ListBox callStackListBox;
    private readonly TreeGridView codeTreeGridView;
    private readonly Command continueCommand;
    private readonly RichTextArea outputTextArea;
    private readonly Command runCommand;
    private readonly Runtime runtime;
    private readonly ListBox stackListBox;
    private readonly Command stepCommand;
    private readonly UITimer timer = new UITimer();
    private bool underline;
    private bool bold;
    private bool firstElapsed = true;

    public Editor(Application application, string baseDirectory, string codeFilename)
    {
      // UI:
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
      Button runButton = new Button { Command = runCommand, Text = RunText };
      Button continueButton = new Button { Command = continueCommand, Text = ContinueText };
      Button stepButton = new Button { Command = stepCommand, Text = StepText };
      stackListBox = new ListBox { Width = StandardDimension };
      callStackListBox = new ListBox { Width = StandardDimension };
      callStackListBox.SelectedIndexChanged += OnCallStackListBoxSelectedIndexChanged;
      codeTreeGridView = NewTreeGridView();
      outputTextArea = new RichTextArea { Height = /*StandardDimension*/ 350 };
      TableLayout buttonLayout = TableLayout.Horizontal(runButton, continueButton, stepButton, new Panel());
      TableRow codeTableRow = new TableRow(codeTreeGridView) { ScaleHeight = true };
      TableCell middleCell = new TableCell(new TableLayout(buttonLayout, codeTableRow, outputTextArea)) { ScaleWidth = true };
      Content = new TableRow(stackListBox, middleCell, callStackListBox);

      outputTextArea.SelectionUnderline = true;

      // Other initializations:
      runtime = new Runtime(application, baseDirectory);
      runtime.Breaking += UpdateUI;
      runtime.Outputting += OnOutputting;
      runtime.Stepping += UpdateUI;
      runtime.Terminating += OnTerminating;
      runtime.Code = codeFilename;
      timer.Interval = 0.01;
      timer.Elapsed += OnElapsed;
      timer.Start();
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
      codeGridColumn.HeaderText = HeaderTextCode;
      result.Columns.Add(codeGridColumn);
      GridColumn typeGridColumn = new GridColumn();
      typeGridColumn.Editable = false;
      typeGridColumn.DataCell = new TextBoxCell(1);
      typeGridColumn.Resizable = false;
      typeGridColumn.HeaderText = HeaderTextType;
      result.Columns.Add(typeGridColumn);
      return result;
    }

    private void OnCallStackListBoxSelectedIndexChanged(object sender, EventArgs e)
    {
      int index = callStackListBox.SelectedIndex;
      if (0 <= index && index < callStackListBox.Items.Count)
      {
        ListItem selectedItem = (ListItem) callStackListBox.Items[index];
        CallEnvironment callEnvironment = (CallEnvironment) selectedItem.Tag;
        RebuildCodeTreeView(callEnvironment.Items, callEnvironment.CurrentItem);
      }
    }

    private void OnContinue(object sender, EventArgs e)
    {
      callStackListBox.Items.Clear();
      runtime.Continue(false);
    }

    private void OnElapsed(object sender, EventArgs e)
    {
      runCommand.Enabled = !runtime.Running;
      continueCommand.Enabled = runtime.Paused;
      stepCommand.Enabled = runtime.Paused;
      if (firstElapsed)
      {
        firstElapsed = false;
        timer.Interval = 0.25;
        UpdateUI();
      }
    }

    private void OnOutputting(object sender, string message)
    {
      switch (message)
      {
        case "+b":
          bold = true;
          break;
        case "-b":
          bold = false;
          break;
        case "+u":
          underline = true;
          break;
        case "-u":
          underline = false;
          break;
        default:
          outputTextArea.SelectionBold = bold;
          outputTextArea.SelectionUnderline = underline;
          outputTextArea.Append(message);
          break;
      }
    }

    private void OnRun(object sender, EventArgs arguments)
    {
      runtime.Run();
    }

    private void OnStep(object sender, EventArgs e)
    {
      callStackListBox.Items.Clear();
      runtime.Continue(true);
    }

    private void OnTerminating(object sender, Exception exception)
    {
      if (exception != null)
      {
        outputTextArea.Append(exception.Message + Environment.NewLine);
      }
      UpdateUI();
    }

    private void RebuildCallStackListBox(CallEnvironments callEnvironments)
    {
      callStackListBox.Items.Clear();
      foreach (CallEnvironment currentCallEnvironment in callEnvironments)
      {
        ListItem newListItem = new ListItem();
        newListItem.Text = currentCallEnvironment.CurrentItem?.ToString() ?? "(null)";
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
        stackListBox.Items.Add(currentValue.GetType().Name + " " + currentValue);
      }
    }

    private void UpdateUI()
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