using EasyNdf.App.Editor;

namespace EasyNdf.App.Tests;

/// <summary>
/// What the File Editor's tips for coloured words read from the text: which coloured word a place is on, a keyword's
/// note, a template's values, a template value's type and default, and an object's first settings.
/// </summary>
public sealed class SyntaxWordsTests
{
    private const string Template = """
        private XpLeftOffset is 10.0

        template DebriefXpLine
        [
            LineName : string,
            MagnifiableHeight : float = 24.0,   // the height
            VerticalAlignment : int = ~/UIText_VerticalCenter,
            Frame : TUIFramePropertyRTTI = TUIFramePropertyRTTI(MagnifiableOffset = [0.0, 1.0]),
        ]
        is BUCKContainerDescriptor
        (
            MagnifiableWidthHeight = [400.0, <MagnifiableHeight>]
        )
        """;

    private const string Ammunition = """
        export Ammo_Small is TAmmunitionDescriptor
        (
            DescriptorId = GUID:{248d1f65-41a9-4dfe-a9f9-c100de0901ba}
            Name = 'None'
            Caliber = ''
            Arme = TDamageTypeRTTI(Family=DamageFamily_he Index=1)
            ProjectileType = EProjectileType/Fake
            TraitsToken = [ ]
            WeaponCursorType = Weapon_Cursor_None
            FxPower = EFXPower/Moyen
            ImpactHappening = 'DestructionUnitePetit'
            MaximumRangeGRU = 150
        )
        export Ammo_Next is TAmmunitionDescriptor
        (
            MaximumRangeGRU = 300
        )
        """;

    [Fact]
    public void The_coloured_word_under_a_place_is_found_as_the_editor_colours_it()
    {
        const string line = "export Ammo_X is TAmmunitionDescriptor";
        Assert.Equal(("keyword", "export"), Of(SyntaxWords.PartAt(line, 0, 2)));
        Assert.Equal(("name", "Ammo_X"), Of(SyntaxWords.PartAt(line, 0, 9)));
        Assert.Equal(("keyword", "is"), Of(SyntaxWords.PartAt(line, 0, 14)));
        Assert.Equal(("type", "TAmmunitionDescriptor"), Of(SyntaxWords.PartAt(line, 0, 20)));
        Assert.Null(SyntaxWords.PartAt(line, 0, 6));
        Assert.Equal(("ref", "$/GFX/Weapon/Ammo_X"), Of(SyntaxWords.PartAt("    DeathExplosionAmmo = $/GFX/Weapon/Ammo_X", 0, 30)));
        Assert.Equal(("param", "<MagnifiableHeight>"), Of(SyntaxWords.PartAt("    W = [400.0, <MagnifiableHeight>]", 0, 20)));
    }

    private static (string, string)? Of((Engine.Editing.ColouredPart Part, string Word)? found) => found is { } f ? (f.Part.Colour, f.Word) : null;

    [Fact]
    public void A_brace_comment_is_headed_with_its_braces_and_other_comments_with_their_mark()
    {
        Assert.Equal("{}", SyntaxWords.CommentMark("{test}", insideBraces: false));
        Assert.Equal("{}", SyntaxWords.CommentMark("{9690a5e1-8a3e-495d-b9c1-35d11b02fad9}", insideBraces: false));
        Assert.Equal("{}", SyntaxWords.CommentMark("rest of a note }", insideBraces: true));
        Assert.Equal("//", SyntaxWords.CommentMark("// a note", insideBraces: false));
        Assert.Equal("/*", SyntaxWords.CommentMark("/* a note */", insideBraces: false));
    }

    [Fact]
    public void Keywords_have_notes_whatever_their_capitals_and_other_words_have_none()
    {
        Assert.Equal("Editor.Syntax.Keyword.export", SyntaxWords.KeywordNote("Export"));
        Assert.Equal("Editor.Syntax.Keyword.boolean", SyntaxWords.KeywordNote("bool"));
        Assert.Equal("Editor.Syntax.Keyword.map", SyntaxWords.KeywordNote("MAP"));
        Assert.Equal("Editor.Syntax.Keyword.logic", SyntaxWords.KeywordNote("and"));
        Assert.Null(SyntaxWords.KeywordNote("Ammo_X"));
    }

    [Fact]
    public void A_reference_points_at_its_last_part()
    {
        Assert.Equal("Ammo_X", SyntaxWords.ReferredName("$/GFX/Weapon/Ammo_X"));
        Assert.Equal("UIText_VerticalCenter", SyntaxWords.ReferredName("~/UIText_VerticalCenter"));
    }

    [Fact]
    public void A_template_gives_its_values_their_types_and_defaults_and_what_it_is_built_on()
    {
        TemplateSignature template = SyntaxWords.TemplateIn(Template, "DebriefXpLine")!;
        Assert.Equal("BUCKContainerDescriptor", template.BuiltOn);
        Assert.Equal(["LineName", "MagnifiableHeight", "VerticalAlignment", "Frame"], template.Values.Select(v => v.Name));
        Assert.Equal(new TemplateValue("LineName", "string", null), template.Values[0]);
        Assert.Equal(new TemplateValue("MagnifiableHeight", "float", "24.0"), template.Values[1]);
        Assert.Equal(new TemplateValue("VerticalAlignment", "int", "~/UIText_VerticalCenter"), template.Values[2]);
        Assert.Equal("TUIFramePropertyRTTI(MagnifiableOffset = [0.0, 1.0])", template.Values[3].Default);
        Assert.Null(SyntaxWords.TemplateIn(Template, "OtherTemplate"));
    }

    [Fact]
    public void A_template_value_in_the_body_belongs_to_the_template_around_it()
    {
        int at = Template.IndexOf("<MagnifiableHeight>", StringComparison.Ordinal);
        Assert.Equal("DebriefXpLine", SyntaxWords.TemplateAround(Template, at)!.Name);
        Assert.Null(SyntaxWords.TemplateAround(Template, Template.IndexOf("XpLeftOffset", StringComparison.Ordinal)));
    }

    [Fact]
    public void Lines_say_what_they_declare()
    {
        Assert.Equal("XpLeftOffset", SyntaxWords.DeclaredOn("private XpLeftOffset is 10.0"));
        Assert.Equal("MagnifiableHeight", SyntaxWords.ValueDeclaredOn("    MagnifiableHeight : float = 24.0,"));
        Assert.Equal("TAmmunitionDescriptor", SyntaxWords.TypeDeclaredOn("export Ammo_Small is TAmmunitionDescriptor", "Ammo_Small"));
        Assert.Null(SyntaxWords.TypeDeclaredOn("private XpLeftOffset is 10.0", "XpLeftOffset"));
    }

    [Fact]
    public void An_object_shows_its_first_settings_with_a_value_and_counts_the_rest()
    {
        (int start, int end) = SyntaxWords.DeclarationIn(Ammunition, "Ammo_Small")!.Value;
        Assert.StartsWith("export Ammo_Small", Ammunition[start..end]);
        Assert.DoesNotContain("Ammo_Next", Ammunition[start..end]);
        KeyValues values = SyntaxWords.KeyValuesOf(Ammunition[start..end])!;
        // its id, the empty text, 'None', the empty list and the None choice are left out
        Assert.Equal(["Arme", "ProjectileType", "FxPower", "ImpactHappening"], values.Shown.Select(v => v.Key));
        Assert.Equal("TDamageTypeRTTI(Family=DamageFamily_he Index=1)", values.Shown[0].Value);
        Assert.Equal(10, values.Total);
        Assert.Null(values.ConstantValue);
    }

    [Fact]
    public void A_constant_shows_its_value()
    {
        (int start, int end) = SyntaxWords.DeclarationIn(Template, "XpLeftOffset")!.Value;
        Assert.Equal("10.0", SyntaxWords.KeyValuesOf(Template[start..end])!.ConstantValue);
    }

    [Fact]
    public void A_word_is_counted_only_where_it_stands_alone()
    {
        Assert.Equal(2, SyntaxWords.CountOf("A is TFoo\nB is TFoo(X = TFooBar)\nC = $/Path/TFoo", "TFoo"));
        Assert.Equal(3, SyntaxWords.LineOf("a\nb\nc", 4));
    }
}
