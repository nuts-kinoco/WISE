using System;
using System.Collections.Generic;
using WISE.Domain.Enums;
using WISE.Domain.SeedWork;

namespace WISE.Domain.Entities;

public class DisplayProfile : Entity
{
    public MediaType MediaType { get; private set; }
    public string CoverOrientation { get; private set; }
    public string DefaultSort { get; private set; }
    public bool IsUserCustomized { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private readonly List<DisplayProfileField> _fields = new();
    public virtual IReadOnlyCollection<DisplayProfileField> Fields => _fields.AsReadOnly();

    protected DisplayProfile()
    {
        CoverOrientation = "portrait";
        DefaultSort = "created_at DESC";
    }

    public DisplayProfile(MediaType mediaType, string coverOrientation, string defaultSort)
    {
        Id = Guid.NewGuid();
        MediaType = mediaType;
        CoverOrientation = coverOrientation ?? "portrait";
        DefaultSort = defaultSort ?? "created_at DESC";
        IsUserCustomized = false;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void AddField(DisplayProfileField field)
    {
        if (field == null) throw new ArgumentNullException(nameof(field));
        _fields.Add(field);
    }

    public void MarkAsCustomized()
    {
        IsUserCustomized = true;
        UpdatedAt = DateTime.UtcNow;
    }

    public void ResetToDefault()
    {
        IsUserCustomized = false;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateCoverOrientation(string orientation)
    {
        CoverOrientation = orientation;
        UpdatedAt = DateTime.UtcNow;
    }
}

public class DisplayProfileField : Entity
{
    public Guid ProfileId { get; private set; }
    public string FieldName { get; private set; }
    public string Label { get; private set; }
    public bool IsVisible { get; private set; }
    public int DisplayOrder { get; private set; }

    public virtual DisplayProfile? Profile { get; private set; }

    protected DisplayProfileField()
    {
        FieldName = string.Empty;
        Label = string.Empty;
    }

    public DisplayProfileField(Guid profileId, string fieldName, string label, bool isVisible, int displayOrder)
    {
        Id = Guid.NewGuid();
        ProfileId = profileId;
        FieldName = fieldName ?? throw new ArgumentNullException(nameof(fieldName));
        Label = label ?? throw new ArgumentNullException(nameof(label));
        IsVisible = isVisible;
        DisplayOrder = displayOrder;
    }

    public void Update(bool isVisible, int displayOrder, string? label = null)
    {
        IsVisible = isVisible;
        DisplayOrder = displayOrder;
        if (label != null) Label = label;
    }
}
