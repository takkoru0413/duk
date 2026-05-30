using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Folding;

namespace duk.App;

public class BraceFoldingStrategy
{
    public void UpdateFoldings(FoldingManager manager, TextDocument document)
    {
        var foldings = CreateFoldings(document).OrderBy(f => f.StartOffset);
        manager.UpdateFoldings(foldings, -1);
    }

    private static IEnumerable<NewFolding> CreateFoldings(TextDocument document)
    {
        var foldings = new List<NewFolding>();
        var stack    = new Stack<int>();
        var text     = document.Text;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '{')
            {
                stack.Push(i);
            }
            else if (c == '}' && stack.Count > 0)
            {
                int start = stack.Pop();
                if (i - start > 1)
                    foldings.Add(new NewFolding(start, i + 1));
            }
        }
        return foldings;
    }
}
