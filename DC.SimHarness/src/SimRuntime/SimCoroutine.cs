using System.Collections;
using System.Collections.Generic;

namespace Dc.SimHarness.Runtime;

/// The synchronous coroutine pump, injected into the shimmed MonoBehaviour.StartCoroutine
/// so every coroutine the game starts internally (damage routines, ability sequences, …)
/// runs to completion immediately, with no Unity player and no frame waits.
public static class SimCoroutine
{
    /// Safety cap: a headless WaitWhile/WaitUntil whose condition never flips would spin
    /// forever. Break out well before that becomes a hang; the fight is then treated as
    /// degenerate/non-terminating by the driver.
    public const long MaxSteps = 5_000_000;

    /// Last exception swallowed from a drained coroutine (for headless debugging).
    public static System.Exception? LastError;

    public static void Run(IEnumerator? root)
    {
        if (root == null) return;
        var stack = new Stack<IEnumerator>();
        stack.Push(root);
        long steps = 0;
        while (stack.Count > 0)
        {
            if (++steps > MaxSteps)
                throw new SimCoroutineOverflowException($"coroutine exceeded {MaxSteps} steps (likely non-terminating)");
            var top = stack.Peek();
            bool moved;
            try { moved = top.MoveNext(); }
            catch (System.Exception ex) { LastError = ex; stack.Pop(); continue; }  // a coroutine that throws just ends (recorded, not hidden)
            if (!moved) { stack.Pop(); continue; }
            // Unity treats `yield return <IEnumerator>` as "run this as a nested coroutine".
            if (top.Current is IEnumerator child) stack.Push(child);
            // every other yield (WaitForSeconds, null, custom instructions) = advance now.
        }
    }
}

public sealed class SimCoroutineOverflowException : System.Exception
{
    public SimCoroutineOverflowException(string message) : base(message) { }
}
