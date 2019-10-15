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
    private readonly TextArea outputTextArea;
    private readonly TreeGridView codeTreeGridView;
    private readonly TreeGridView stackGridView;
    private readonly Runtime Runtime;
    private readonly ManualResetEvent manualResetEvent = new ManualResetEvent(false);

    TreeGridView NewTreeGridView(params string[] headers)
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

    private Editor(params string[] arguments)
    {
      Runtime = new Runtime(Application, arguments[0]);

      Title = "Atom2";
      WindowState = WindowState.Maximized;

      Command runCommand = new Command(OnRun);
      runCommand.MenuText = "&Run";
      Command continueCommand = new Command(OnContinue);
      continueCommand.MenuText = "&Continue";

      ButtonMenuItem fileMenuItem = new ButtonMenuItem();
      fileMenuItem.Text = "&File";
      fileMenuItem.Items.Add(runCommand);
      fileMenuItem.Items.Add(continueCommand);

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
      stackGridView = NewTreeGridView("Value", "Type");

      // Right column:
      codeTreeGridView = NewTreeGridView("Value", "Type");

      // Table row:
      TableRow tableRow = new TableRow(stackGridView, centerTableCell, codeTreeGridView);

      // Layout
      TableLayout layout = new TableLayout();
      layout.Rows.Add(tableRow);
      Content = layout;

      Runtime.Breaking += OnBreaking;
    }

    private void OnContinue(object sender, EventArgs e)
    {
      manualResetEvent.Set();
    }

    private void OnBreaking()
    {
      Application.Invoke(DoBreaking);
      manualResetEvent.WaitOne();
      manualResetEvent.Reset();
      // Thread.Sleep(5000);
    }

    private void DoBreaking()
    {
      CallEnvironment topmostCallEnvironment = Runtime.CallEnvironments.Peek();
      RebuildCodeTreeView(topmostCallEnvironment.Items, topmostCallEnvironment.CurrentItem);
    }

    private void OnRun(object sender, EventArgs arguments)
    {
      Thread thread = new Thread(DoRun);

      thread.Start(codeTextArea.Text);
    }

    private void DoRun(object code)
    {
      Runtime.Run((string) code, out _);
    }

    private void RebuildCodeTreeView(Items items, object executingItem)
    {
      TreeGridItem executingTreeGridItem = null;
      codeTreeGridView.DataStore = RebuildTrackWindow(items, executingItem, ref executingTreeGridItem);
      codeTreeGridView.SelectedItem = executingTreeGridItem;
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

    public static void Run(string[] arguments)
    {
      Application.Run(new Editor(arguments));
    }
  }
}