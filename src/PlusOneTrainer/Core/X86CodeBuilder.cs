namespace PlusOneTrainer.Core;

internal sealed class X86CodeBuilder
{
    private readonly List<byte> _bytes = [];
    private readonly List<(int Offset, uint Target)> _calls = [];

    public X86CodeBuilder MovEax(uint value) => Emit(0xB8).Dword(value);
    public X86CodeBuilder MovEcx(uint value) => Emit(0xB9).Dword(value);
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
