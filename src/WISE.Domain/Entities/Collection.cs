using System;
using System.Collections.Generic;

namespace WISE.Domain.Entities;

public class Collection
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<CollectionItem> Items { get; set; } = new List<CollectionItem>();
}

public class CollectionItem
{
    public Guid Id { get; set; }
    public Guid CollectionId { get; set; }
    public Guid WorkId { get; set; }
    public int Order { get; set; }
    public DateTime AddedAt { get; set; }

    public Collection Collection { get; set; } = null!;
    public Work Work { get; set; } = null!;
}
