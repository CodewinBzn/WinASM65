namespace WinASM65.Execution
{
    /// <summary>
    /// The address space a processor reads and writes through.
    /// <para>
    /// This is the seam between the interpreter and the memory it runs on, and it
    /// exists so the core can be pointed at something other than flat RAM without
    /// being rewritten: a ROM image that only accepts reads, a banking window, a
    /// flat 64K, or a spy that records every access for a debugger.
    /// </para>
    /// <para>
    /// Only what the processor does goes through here. The debugger's own reads,
    /// such as a memory pane refreshing a line, use the same bus but are not
    /// accesses made by the processor, and the core does not report them as
    /// watchpoint hits.
    /// </para>
    /// </summary>
    public interface ICpuBus
    {
        byte Read(ushort address);

        void Write(ushort address, byte value);
    }
}
