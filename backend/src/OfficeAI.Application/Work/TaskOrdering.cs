using System.Linq.Expressions;
using OfficeAI.Domain.Work;

namespace OfficeAI.Application.Work;

public static class TaskOrdering
{
    /// <summary>
    /// Priority as a number the database can sort on. Priority is stored as text, so ordering by the column itself
    /// would be alphabetical (Medium before Critical).
    /// </summary>
    public static readonly Expression<Func<WorkTask, int>> PriorityRank = t =>
        t.Priority == TaskPriority.Critical ? 4 : t.Priority == TaskPriority.High ? 3 : t.Priority == TaskPriority.Medium ? 2 : 1;
}
