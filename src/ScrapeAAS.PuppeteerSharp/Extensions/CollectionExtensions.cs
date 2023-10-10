namespace ScrapeAAS.Extensions;

internal static class CollectionExtensions
{
    public static void SwapRemove<T>(this IList<T> list, int index)
    {
        var last = list.Count - 1;
        list[index] = list[last];
        list.RemoveAt(last);
    }
}
