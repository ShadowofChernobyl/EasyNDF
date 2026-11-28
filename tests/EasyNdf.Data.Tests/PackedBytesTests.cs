using System.Text;
using EasyNdf.Data.FileList;

namespace EasyNdf.Data.Tests;

public class PackedBytesTests
{
    [Fact]
    public void Unpacking_gives_back_the_same_bytes()
    {
        byte[] text = Encoding.UTF8.GetBytes("﻿TAmmo is TAmmunitionDescriptor\r\n(\r\n    Damage = 12.5\r\n)\r\n");

        PackedBytes packed = PackedBytes.Pack(text);

        Assert.Equal(text, packed.Unpack());
        Assert.Equal(text.LongLength, packed.Length);
    }

    [Fact]
    public void Empty_bytes_pack_and_unpack()
    {
        Assert.Empty(PackedBytes.Pack([]).Unpack());
    }

    [Fact]
    public void A_text_is_unpacked_character_for_character()
    {
        // a lone half of a surrogate pair too, which a conversion to UTF-8 would not keep
        string text = "Unit_A is TEntity\n(\n    Name = 'Ünïcode \uD83D'\n)\n";

        Assert.Equal(text, PackedBytes.PackText(text).UnpackText());
    }
}
