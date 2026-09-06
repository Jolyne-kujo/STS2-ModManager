namespace Sts2ModManager.Core;

public static class ModOrderService
{
    public static IReadOnlyList<T> MoveToInsertionIndex<T>(
        IReadOnlyList<T> items,
        int fromIndex,
        int insertionIndex)
    {
        if (fromIndex < 0 || fromIndex >= items.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(fromIndex));
        }

        var result = items.ToList();
        insertionIndex = Math.Clamp(insertionIndex, 0, result.Count);
        var moved = result[fromIndex];
        result.RemoveAt(fromIndex);
        if (insertionIndex > fromIndex)
        {
            insertionIndex--;
        }

        result.Insert(Math.Clamp(insertionIndex, 0, result.Count), moved);
        return result;
    }
}
