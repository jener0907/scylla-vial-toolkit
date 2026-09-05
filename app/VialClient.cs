namespace ScyllaConfigurator;

public sealed class VialClient : IDisposable
{
    private const byte VialPrefix = 0xFE;
    private readonly HidDevice _device;
    public VialClient(HidDevice device) => _device = device;

    public Task<byte[]> SendAsync(byte[] packet, int timeoutMs = 900)
    {
        return Task.Run(() => _device.Exchange(packet)).WaitAsync(TimeSpan.FromMilliseconds(timeoutMs));
    }

    public async Task<(uint Version, byte[] Uid)> GetKeyboardIdAsync()
    {
        var p = new byte[32]; p[0] = VialPrefix; p[1] = 0x00;
        var r = await SendAsync(p);
        var version = (uint)(r[0] | (r[1] << 8) | (r[2] << 16) | (r[3] << 24));
        return (version, r[4..12]);
    }

    public async Task<(bool Unlocked, bool InProgress, (byte Row, byte Col)[] Combo)> GetUnlockStatusAsync()
    {
        var p = new byte[32]; p[0] = VialPrefix; p[1] = 0x05;
        var r = await SendAsync(p);
        var keys = new List<(byte, byte)>();
        for (int i = 0; i < 14; i++)
        {
            var row = r[2 + i * 2]; var col = r[3 + i * 2];
            if (row == 0xFF || col == 0xFF) break;
            keys.Add((row, col));
        }
        return (r[0] != 0, r[1] != 0, keys.ToArray());
    }

    public async Task<bool> UnlockAsync(IProgress<int>? progress = null)
    {
        var start = new byte[32]; start[0] = VialPrefix; start[1] = 0x06;
        await SendAsync(start);
        for (int i = 0; i < 55; i++)
        {
            await Task.Delay(110);
            var poll = new byte[32]; poll[0] = VialPrefix; poll[1] = 0x07;
            var r = await SendAsync(poll);
            progress?.Report(Math.Min(100, i * 100 / 50));
            if (r[0] != 0) return true;
        }
        return false;
    }

    public async Task<ushort> GetKeycodeAsync(int layer, int row, int col)
    {
        var p = new byte[32]; p[0] = 0x04; p[1] = (byte)layer; p[2] = (byte)row; p[3] = (byte)col;
        var r = await SendAsync(p);
        return (ushort)((r[4] << 8) | r[5]);
    }

    public async Task SetKeycodeAsync(int layer, int row, int col, ushort keycode)
    {
        var p = new byte[32]; p[0] = 0x05; p[1] = (byte)layer; p[2] = (byte)row; p[3] = (byte)col; p[4] = (byte)(keycode >> 8); p[5] = (byte)keycode;
        await SendAsync(p);
    }

    public async Task SetKeycodeAndVerifyAsync(int layer, int row, int col, ushort keycode)
    {
        await SetKeycodeAsync(layer, row, col, keycode);
        var actual = await GetKeycodeAsync(layer, row, col);
        if (actual != keycode)
            throw new InvalidOperationException($"키코드 저장 검증 실패: ({row},{col})에 0x{keycode:X4} 대신 0x{actual:X4}가 읽혔습니다.");
    }

    public async Task ResetKeymapAsync()
    {
        var p = new byte[32]; p[0] = 0x06;
        await SendAsync(p);
    }

    public async Task<int> GetMacroCountAsync()
    {
        var p = new byte[32]; p[0] = 0x0C;
        var r = await SendAsync(p);
        return r[1];
    }

    public async Task<int> GetMacroBufferSizeAsync()
    {
        var p = new byte[32]; p[0] = 0x0D;
        var r = await SendAsync(p);
        return (r[1] << 8) | r[2];
    }

    public async Task<byte[]> GetMacroBufferAsync()
    {
        var size = await GetMacroBufferSizeAsync();
        var buffer = new byte[size];
        for (var offset = 0; offset < size; offset += 28)
        {
            var chunkSize = Math.Min(28, size - offset);
            var p = new byte[32];
            p[0] = 0x0E;
            p[1] = (byte)(offset >> 8);
            p[2] = (byte)offset;
            p[3] = (byte)chunkSize;
            var r = await SendAsync(p);
            Array.Copy(r, 4, buffer, offset, chunkSize);
        }
        return buffer;
    }

    public async Task SaveMacroAsync(int slot, byte[] macro)
    {
        var count = await GetMacroCountAsync();
        var size = await GetMacroBufferSizeAsync();
        if (slot < 0 || slot >= count) throw new InvalidOperationException("매크로 슬롯을 읽을 수 없습니다.");
        if (size < count || macro.Length >= size) throw new InvalidOperationException("매크로 버퍼 공간이 부족합니다.");

        var current = await GetMacroBufferAsync();
        var macros = new List<byte[]>();
        var offset = 0;
        for (var i = 0; i < count && offset < current.Length; i++)
        {
            var end = Array.IndexOf(current, (byte)0, offset);
            if (end < 0) break;
            macros.Add(current[offset..end]);
            offset = end + 1;
        }
        while (macros.Count < count) macros.Add([]);
        macros[slot] = macro;

        var next = new byte[size];
        var writeOffset = 0;
        foreach (var item in macros)
        {
            if (writeOffset + item.Length + 1 > size) throw new InvalidOperationException("매크로 버퍼 공간이 부족합니다.");
            Array.Copy(item, 0, next, writeOffset, item.Length);
            writeOffset += item.Length;
            next[writeOffset++] = 0;
        }

        var invalid = (byte[])next.Clone();
        invalid[^1] = 0xFF;
        await SetMacroBufferAsync(invalid);
        await SetMacroBufferChunkAsync(size - 1, 1, [0]);

        var saved = GetMacro(await GetMacroBufferAsync(), slot);
        if (!saved.SequenceEqual(macro))
        {
            var lockState = await GetUnlockStatusAsync();
            throw new InvalidOperationException(lockState.Unlocked
                ? "장치에서 다시 읽은 매크로가 저장한 내용과 다릅니다."
                : "Vial이 잠겨 있어 장치가 저장 명령을 거부했습니다.");
        }
    }

    private static byte[] GetMacro(byte[] buffer, int slot)
    {
        var start = 0;
        for (var i = 0; i < slot; i++)
        {
            var separator = Array.IndexOf(buffer, (byte)0, start);
            if (separator < 0) return [];
            start = separator + 1;
        }
        var end = Array.IndexOf(buffer, (byte)0, start);
        return end < 0 ? [] : buffer[start..end];
    }

    private async Task SetMacroBufferAsync(byte[] data)
    {
        for (var offset = 0; offset < data.Length; offset += 28)
            await SetMacroBufferChunkAsync(offset, Math.Min(28, data.Length - offset), data[offset..]);
    }

    private async Task SetMacroBufferChunkAsync(int offset, int size, byte[] data)
    {
        var p = new byte[32];
        p[0] = 0x0F;
        p[1] = (byte)(offset >> 8);
        p[2] = (byte)offset;
        p[3] = (byte)size;
        Array.Copy(data, 0, p, 4, size);
        await SendAsync(p);
    }

    public void Dispose() => _device.Dispose();
}
