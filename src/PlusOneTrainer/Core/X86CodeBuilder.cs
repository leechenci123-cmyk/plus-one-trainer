namespace PlusOneTrainer.Core;

internal sealed class X86CodeBuilder
{
    private readonly List<byte> _bytes = [];
    private readonly List<(int Offset, uint Target)> _calls = [];
    private readonly Dictionary<string, int> _labels = new(StringComparer.Ordinal);
    private readonly List<(int Offset, string Label)> _branches = [];

    public X86CodeBuilder MovEax(uint value) => Emit(0xB8).Dword(value);
    public X86CodeBuilder MovEcx(uint value) => Emit(0xB9).Dword(value);
    public X86CodeBuilder MovEdx(uint value) => Emit(0xBA).Dword(value);
    public X86CodeBuilder MovEsi(uint value) => Emit(0xBE).Dword(value);
    public X86CodeBuilder MovEdi(uint value) => Emit(0xBF).Dword(value);
    public X86CodeBuilder MovEaxFromAbsolute(uint address) => Emit(0xA1).Dword(address);
    public X86CodeBuilder MovAbsoluteFromEax(uint address) => Emit(0xA3).Dword(address);
    public X86CodeBuilder MovEcxFromAbsolute(uint address) => Emit(0x8B, 0x0D).Dword(address);
    public X86CodeBuilder MovEsiFromAbsolute(uint address) => Emit(0x8B, 0x35).Dword(address);
    public X86CodeBuilder MovEaxFromEax(uint offset) => Emit(0x8B, 0x80).Dword(offset);
    public X86CodeBuilder MovEcxFromEcx(uint offset) => Emit(0x8B, 0x89).Dword(offset);
    public X86CodeBuilder MovEsiFromEsi(uint offset) => Emit(0x8B, 0xB6).Dword(offset);
    public X86CodeBuilder MovDwordAtEsi(uint offset, uint value) => Emit(0xC7, 0x86).Dword(offset).Dword(value);
    public X86CodeBuilder Push(uint value) => Emit(0x68).Dword(value);
    public X86CodeBuilder Ret() => Emit(0xC3);
    public X86CodeBuilder PreserveRegisters() => Emit(0x9C, 0x60); // pushfd; pushad
    public X86CodeBuilder RestoreRegisters() => Emit(0x61, 0x9D); // popad; popfd
    public X86CodeBuilder ReturnThread() => Emit(0x31, 0xC0, 0xC2, 0x04, 0x00);
    public X86CodeBuilder Bytes(params byte[] bytes) => Emit(bytes);
    public X86CodeBuilder UInt32(uint value) => Dword(value);

    public X86CodeBuilder Label(string name)
    {
        if (!_labels.TryAdd(name, _bytes.Count))
            throw new ArgumentException($"Duplicate x86 label: {name}");
        return this;
    }

    public X86CodeBuilder Jump(string label) => Branch(label, 0xE9);
    public X86CodeBuilder JumpIfEqual(string label) => Branch(label, 0x0F, 0x84);
    public X86CodeBuilder JumpIfNotEqual(string label) => Branch(label, 0x0F, 0x85);
    public X86CodeBuilder JumpIfAbove(string label) => Branch(label, 0x0F, 0x87);
    public X86CodeBuilder JumpIfBelow(string label) => Branch(label, 0x0F, 0x82);

    private X86CodeBuilder Branch(string label, params byte[] opcode)
    {
        Emit(opcode);
        _branches.Add((_bytes.Count, label));
        return Dword(0);
    }

    public X86CodeBuilder Jump(uint target)
    {
        Emit(0xE9);
        _calls.Add((_bytes.Count, target));
        return Dword(0);
    }

    public X86CodeBuilder Call(uint target)
    {
        Emit(0xE8);
        _calls.Add((_bytes.Count, target));
        Dword(0);
        return this;
    }

    public byte[] Build(uint remoteAddress)
    {
        var result = _bytes.ToArray();
        foreach (var call in _calls)
        {
            var nextInstruction = remoteAddress + (uint)call.Offset + 4;
            var relative = unchecked((int)(call.Target - nextInstruction));
            BitConverter.GetBytes(relative).CopyTo(result, call.Offset);
        }
        foreach (var branch in _branches)
        {
            if (!_labels.TryGetValue(branch.Label, out var target))
                throw new InvalidOperationException($"Undefined x86 label: {branch.Label}");
            BitConverter.GetBytes(target - branch.Offset - 4).CopyTo(result, branch.Offset);
        }
        return result;
    }

    private X86CodeBuilder Emit(params byte[] bytes)
    {
        _bytes.AddRange(bytes);
        return this;
    }

    private X86CodeBuilder Dword(uint value)
    {
        _bytes.AddRange(BitConverter.GetBytes(value));
        return this;
    }
}
