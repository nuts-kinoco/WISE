using System;
using System.Collections.Generic;
using WISE.Domain.Events;

namespace WISE.Domain.SeedWork;

public abstract class Entity
{
    int? _requestedHashCode;
    Guid _Id;
    
    private List<IDomainEvent>? _domainEvents;
    public IReadOnlyCollection<IDomainEvent>? DomainEvents => _domainEvents?.AsReadOnly();

    public void AddDomainEvent(IDomainEvent eventItem)
    {
        _domainEvents ??= new List<IDomainEvent>();
        _domainEvents.Add(eventItem);
    }

    public void RemoveDomainEvent(IDomainEvent eventItem)
    {
        _domainEvents?.Remove(eventItem);
    }

    public void ClearDomainEvents()
    {
        _domainEvents?.Clear();
    }

    public virtual Guid Id
    {
        get => _Id;
        protected set => _Id = value;
    }

    public bool IsTransient()
    {
        return this.Id == default;
    }

    public override bool Equals(object? obj)
    {
        if (obj == null || !(obj is Entity))
            return false;
            
        if (ReferenceEquals(this, obj))
            return true;
            
        if (this.GetType() != obj.GetType())
            return false;
            
        Entity item = (Entity)obj;
        
        if (item.IsTransient() || this.IsTransient())
            return false;
        else
            return item.Id == this.Id;
    }

    public override int GetHashCode()
    {
        if (!IsTransient())
        {
            if (!_requestedHashCode.HasValue)
                _requestedHashCode = this.Id.GetHashCode() ^ 31;

            return _requestedHashCode.Value;
        }
        else
            return base.GetHashCode();
    }

    public static bool operator ==(Entity? left, Entity? right)
    {
        if (Object.Equals(left, null))
            return (Object.Equals(right, null)) ? true : false;
        else
            return left.Equals(right);
    }

    public static bool operator !=(Entity? left, Entity? right)
    {
        return !(left == right);
    }
}
