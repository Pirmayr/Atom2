namespace Mira
{
  using System;
  using System.Collections.Generic;
  using System.IO;
  using Eto.Forms;
  using Eto.Mac.Forms.Controls;
  using MonoMac.AppKit;

  public sealed class Editor : Form
  {
    private const string ContinueText = "Continue";
    private const string RunText = "Run";
    private const int StandardDimension = 300;
    private const string StepText = "Step";
    private const string TitleText = "Mira";
    private readonly ListBox callStackListBox;
    private readonly TreeGridView codeTreeGridView;
    private readonly Command continueCommand;
    private readonly RichTextArea outputTextArea;
    private readonly Command runCommand;
    private readonly Mira runtime;
    private readonly ListBox stackListBox;
    private readonly WebView webView;
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
      stackListBox = new ListBox { Height = StandardDimension, Width = StandardDimension, Style = "ListNative" };
      callStackListBox = new ListBox { Width = StandardDimension, Style = "ListNative" };
      callStackListBox.SelectedIndexChanged += OnCallStackListBoxSelectedIndexChanged;
      codeTreeGridView = new TreeGridView() { ShowHeader = false };
      codeTreeGridView.Columns.Add(new GridColumn { Editable = false, DataCell = new TextBoxCell(0), Resizable = false });
      codeTreeGridView.Columns.Add(new GridColumn { Editable = false, DataCell = new TextBoxCell(1), Resizable = false });
      outputTextArea = new RichTextArea { Height = StandardDimension };
      webView = new WebView();
      TableLayout buttons = TableLayout.Horizontal(runButton, continueButton, stepButton, new Panel());
      TableLayout codeControls = TableLayout.Horizontal(callStackListBox, new TableCell(TableLayout.HorizontalScaled(codeTreeGridView, webView), true), stackListBox);
      TableLayout mainControls = new TableLayout(new TableRow(codeControls) { ScaleHeight = true }, outputTextArea);
      Content = new TableLayout(buttons, mainControls);
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
      webView.LoadHtml(File.ReadAllText(baseDirectory + "/Test.html"));
    }

    private static TreeGridItemCollection GetCodeTree(IEnumerable<object> rootItems, object executingItem, ref TreeGridItem executingTreeGridViewItem)
    {
      TreeGridItemCollection result = new TreeGridItemCollection();
      foreach (object currentItem in rootItems)
      {
        TreeGridItem newTreeViewItem = currentItem is Mira.Items currentItems ? new TreeGridItem(GetCodeTree(currentItems, executingItem, ref executingTreeGridViewItem), "(Items)") : new TreeGridItem(currentItem.ToString(), currentItem.GetType().Name);
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
      int index = callStackListBox.SelectedIndex;
      if (0 <= index && index < callStackListBox.Items.Count)
      {
        ListItem selectedItem = (ListItem) callStackListBox.Items[index];
        Mira.CallEnvironment callEnvironment = (Mira.CallEnvironment) selectedItem.Tag;
        RebuildCodeTreeView(callEnvironment.Items, callEnvironment.CurrentItem);
      }
    }

    private void OnContinue(object sender, EventArgs e)
    {
      callStackListBox.Items.Clear();
      runtime.Continue(false);
    }

    private void OnLoadComplete(object sender, EventArgs e) => UpdateUI();

    private void OnElapsed(object sender, EventArgs e)
    {
      runCommand.Enabled = !runtime.Running;
      continueCommand.Enabled = runtime.Paused;
      stepCommand.Enabled = runtime.Paused;
    }

    private void OnOutputting(object sender, string message)
    {
      /*
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
      */

      webView.LoadHtml(message);
    }

    private void OnRun(object sender, EventArgs arguments) => runtime.Run();

    private void OnStep(object sender, EventArgs e)
    {
      callStackListBox.Items.Clear();
      runtime.Continue(true);
    }

    private void OnTerminating(object sender, Exception exception)
    {
      if (exception != null)
      {
        string message = "";
        while (exception != null)
        {
          message += exception.Message + Environment.NewLine;
          exception = exception.InnerException;
        }
        outputTextArea.Append(message);
      }
      UpdateUI();
    }

    private void RebuildCallStackListBox(Mira.CallEnvironmentStack callEnvironments)
    {
      callStackListBox.Items.Clear();
      foreach (Mira.CallEnvironment currentCallEnvironment in callEnvironments)
      {
        ListItem newListItem = new ListItem();
        newListItem.Text = currentCallEnvironment.CurrentItem?.ToString() ?? "(null)";
        newListItem.Tag = currentCallEnvironment;
        callStackListBox.Items.Add(newListItem);
      }
    }

    private void RebuildCodeTreeView(Mira.Items items, object executingItem)
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