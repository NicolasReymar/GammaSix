using System;

public sealed class RuntimeEntityIdAllocator
{
    private int nextId;

    public RuntimeEntityIdAllocator(int firstId = 1)
    {
        Reset(firstId);
    }

    public int Next()
    {
        return nextId++;
    }

    public void Reset(int firstId = 1)
    {
        if (firstId <= 0)
            throw new ArgumentOutOfRangeException(nameof(firstId));
        nextId = firstId;
    }
}
