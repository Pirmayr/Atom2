namespace Mira
{
  using System;
  using System.Collections.Generic;
  using System.IO;
  using Eto.Drawing;
  using Eto.Forms;
  using Eto.Mac.Forms.Controls;
  using MonoMac.AppKit;

  public sealed class Editor : Form
  {
    private const string ContinueText = "Continue";
    private const string RunText = "Run";
    private const int StandardDimension = 250;
    private const string StepText = "Step";
    private const string TitleText = "Mira";
    private readonly ListBox frameStack;
    private readonly TreeGridView codeTree;
    private readonly Command continueCommand;
    private readonly RichTextArea outputArea;
    private readonly Command runCommand;
    private readonly Mira runtime;
    private readonly ListBox valueStack;
    private readonly WebView documentationView;
    private readonly RichTextArea programEdit;
    private readonly RichTextArea systemEdit;
    private readonly Command stepCommand;
    private readonly UITimer timer = new UITimer();
    private bool underline;
    private bool bold;

    public Editor(Application application, string baseDirectory, string codeFilename)
    {
      WindowState = WindowState.Maximized;
      Title = TitleText;
      Menu = new MenuBar { IncludeSystemItems = MenuBarSystemItems.Quit };
      runCommand = new Command(OnRun);
      continueCommand = new Command(OnContinue);
      stepCommand = new Command(OnStep) { Shortcut = Keys.F10 };
      Button runButton = new Button { Command = runCommand, Text = RunText };
      Button continueButton = new Button { Command = continueCommand, Text = ContinueText };
      Button stepButton = new Button { Command = stepCommand, Text = StepText };
      systemEdit = new RichTextArea();
      systemEdit.Text = File.ReadAllText(baseDirectory + "/System.txt");
      programEdit = new RichTextArea();
      programEdit.Text = File.ReadAllText(baseDirectory + "/Program.txt");
      codeTree = new TreeGridView() { ShowHeader = false, Width = StandardDimension };
      codeTree.Border = BorderType.Line;
      codeTree.Columns.Add(new GridColumn { Editable = false, DataCell = new TextBoxCell(0), Resizable = false });
      codeTree.Columns.Add(new GridColumn { Editable = false, DataCell = new TextBoxCell(1), Resizable = false });
      codeTree.SelectedItemChanged += OnCodeTreeViewSelectedItemChanged;
      frameStack = new ListBox { Style = "ListNative" };
      frameStack.SelectedIndexChanged += OnCallStackListBoxSelectedIndexChanged;
      valueStack = new ListBox { Style = "ListNative" };
      outputArea = new RichTextArea { Height = StandardDimension };
      documentationView = new WebView();
      Scrollable documentationwindow = new Scrollable();
      documentationwindow.Content = documentationView;
      TableLayout buttons = TableLayout.Horizontal(runButton, continueButton, stepButton, new Panel());
      TableLayout edits = new TableLayout(systemEdit, programEdit);
      edits.SetRowScale(0);
      edits.SetRowScale(1);
      TableLayout stacks = new TableLayout(frameStack, valueStack);
      stacks.SetRowScale(0);
      stacks.SetRowScale(1);
      stacks.Width = StandardDimension;
      TableLayout codeControls = TableLayout.Horizontal(codeTree, edits, stacks);
      codeControls.SetColumnScale(1);
      TableLayout outputControls = TableLayout.HorizontalScaled(outputArea, documentationwindow);
      outputControls.Height = StandardDimension;
      TableLayout mainControls = new TableLayout(buttons, codeControls, outputControls);
      mainControls.SetRowScale(1);
      Content = mainControls;
      runtime = new Mira(application, baseDirectory);
      runtime.Breaking += UpdateUI;
      runtime.Outputting += OnOutputting;
      runtime.Stepping += UpdateUI;
      runtime.Terminating += OnTerminating;
      runtime.Code = codeFilename;
      timer.Interval = 0.33;
      timer.Elapsed += OnElapsed;
      timer.Start();
      LoadComplete += OnLoadComplete;

    }

    private void OnCodeTreeViewSelectedItemChanged(object sender, EventArgs e)
    {
      if ((codeTree.SelectedItem as TreeGridItem)?.Tag is Mira.Name selectedName)
      {
        runtime.Evaluate(runtime.GetItems($"\"{selectedName.ToString()}\" showDocumentation"));
      }
    }

    private static TreeGridItemCollection GetCodeTree(IEnumerable<object> rootItems, object executingItem, ref TreeGridItem executingTreeGridViewItem)
    {
      TreeGridItemCollection result = new TreeGridItemCollection();
      foreach (object currentItem in rootItems)
      {
        TreeGridItem newTreeViewItem = currentItem is Mira.Items currentItems ? new TreeGridItem(GetCodeTree(currentItems, executingItem, ref executingTreeGridViewItem), "(Items)") : new TreeGridItem(currentItem.ToString(), currentItem.GetType().Name);
        newTreeViewItem.Tag = currentItem;
        newTreeViewItem.Expanded = true;
        result.Add(newTreeViewItem);
        if (currentItem == executingItem)
        {
          executingTreeGridViewItem = newTreeViewItem;
        }
      }
      return result;
    }

    private void OnCallStackListBoxSelectedIndexChanged(object sender, EventArgs e)
    {
      int index = frameStack.SelectedIndex;
      if (0 <= index && index < frameStack.Items.Count)
      {
        ListItem selectedItem = (ListItem) frameStack.Items[index];
        Mira.CallEnvironment callEnvironment = (Mira.CallEnvironment) selectedItem.Tag;
        RebuildCodeTreeView(callEnvironment.Items, callEnvironment.CurrentItem);
      }
    }

    private void OnContinue(object sender, EventArgs e)
    {
      frameStack.Items.Clear();
      runtime.Continue(false);
    }

    private void OnLoadComplete(object sender, EventArgs e) => UpdateUI();

    private void OnElapsed(object sender, EventArgs e)
    {
      runCommand.Enabled = !runtime.Running;
      continueCommand.Enabled = runtime.Paused;
      stepCommand.Enabled = runtime.Paused;
    }

    private void OnOutputting(object sender, Mira.OutputtingEventArgs arguments)
    {
      switch (arguments.Target)
      {
        case 0:
          switch (arguments.Message)
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
              outputArea.SelectionBold = bold;
              outputArea.SelectionUnderline = underline;
              outputArea.Append(arguments.Message);
              break;
          }
          break;
        case 1:
          documentationView.LoadHtml(arguments.Message);
          break;
      }
    }

    private void OnRun(object sender, EventArgs arguments) => runtime.Run();

    private void OnStep(object sender, EventArgs e)
    {
      frameStack.Items.Clear();
      runtime.Continue(true);
    }

    public static Exception InnermostException(Exception exception)
    {
      Exception result = exception;
      while (result?.InnerException != null)
      {
        result = result.InnerException;
      }
      return result;
    }

    private void OnTerminating(object sender, Exception exception)
    {
      if (exception != null)
      {
        outputArea.Append(InnermostException(exception).Message);
      }
      UpdateUI();
    }

    private void RebuildCallStackListBox(Mira.CallEnvironmentStack callEnvironments)
    {
      frameStack.Items.Clear();
      foreach (Mira.CallEnvironment currentCallEnvironment in callEnvironments)
      {
        ListItem newListItem = new ListItem();
        newListItem.Text = currentCallEnvironment.CurrentItem?.ToString() ?? "(null)";
        newListItem.Tag = currentCallEnvironment;
        frameStack.Items.Add(newListItem);
      }
    }

    private void RebuildCodeTreeView(Mira.Items items, object executingItem)
    {
      TreeGridItem executingTreeGridItem = null;
      codeTree.DataStore = GetCodeTree(items, executingItem, ref executingTreeGridItem);
      codeTree.SelectedItem = executingTreeGridItem;
    }

    private void RebuildStackListBox()
    {
      valueStack.Items.Clear();
      foreach (object currentValue in runtime.Stack)
      {
        valueStack.Items.Add(currentValue.GetType().Name + " " + currentValue);
      }
    }

    private void UpdateUI()
    {
      Mira.CallEnvironmentStack callEnvironments = runtime.CallEnvironments;
      RebuildCallStackListBox(callEnvironments);
      Mira.CallEnvironment topmostCallEnvironment = callEnvironments.Count == 0 ? null : callEnvironments.Peek();
      Mira.Items topMostItems = topmostCallEnvironment == null ? runtime.CurrentRootItems : topmostCallEnvironment.Items;
      object topMostCurrentItem = topmostCallEnvironment?.CurrentItem;
      RebuildCodeTreeView(topMostItems, topMostCurrentItem);
      RebuildStackListBox();
    }
  }
}