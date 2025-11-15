using NDFParser;
using NDFParser.AST;
using System.Security.Cryptography.X509Certificates;

namespace NDFParserTests
{
    public class ParserTests
    {
        [Fact]
        public void TestAssignmentDeclaration()
        {
            string input = """
                Variable is nil
                """;

            FileDeclaration expected = new FileDeclaration([new AssignDeclaration(false, "Variable", new NilLiteral())]);

            FileDeclaration actual = Parser.ParseFromString(input);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void TestObjectDeclaration()
        {
            string input = """
                Ammo_RocketAir_Hydra_APKWS_x7 is TAmmunitionDescriptor
                (
                    DescriptorId                      = GUID:{7b6e404a-d208-4bfa-a890-4ba861e4968a}
                )
                """;

            FileDeclaration expected = new FileDeclaration([new AssignDeclaration(false, "Ammo_RocketAir_Hydra_APKWS_x7", new ObjectValue
                ( "TAmmunitionDescriptor",
                    [ ("DescriptorId", new GuidLiteral("GUID:{7b6e404a-d208-4bfa-a890-4ba861e4968a}"))
                    ]
                ))]);

            FileDeclaration actual = Parser.ParseFromString(input);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void TestStringDeclaration()
        {
            string input = """
                Ammo_RocketAir_Hydra_APKWS_x7 is TAmmunitionDescriptor
                (
                    Name                              = 'APKWS14'
                    InterfaceWeaponTexture            = "Texture_Interface_Weapon_Hydra_70"
                )
                """;

            FileDeclaration expected = new FileDeclaration([new AssignDeclaration(false, "Ammo_RocketAir_Hydra_APKWS_x7", new ObjectValue
                ( "TAmmunitionDescriptor",
                    [ ("Name", new StringLiteral("'APKWS14'"))
                    , ("InterfaceWeaponTexture", new StringLiteral("\"Texture_Interface_Weapon_Hydra_70\""))
                    ]
                ))]);

            FileDeclaration actual = Parser.ParseFromString(input);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void TestArrayDeclaration()
        {
            string input = """
                Ammo_RocketAir_Hydra_APKWS_x7 is TAmmunitionDescriptor
                (
                    TraitsToken                       = [ 'MOTION', 'HE', 'CLGP', ]
                )
                """;

            FileDeclaration expected = new FileDeclaration(
                [new AssignDeclaration(false, "Ammo_RocketAir_Hydra_APKWS_x7", new ObjectValue( "TAmmunitionDescriptor",
                    [ ("TraitsToken", new ArrayValue([new StringLiteral("'MOTION'"), new StringLiteral("'HE'"), new StringLiteral("'CLGP'")]))
                    ]
                ))]);

            FileDeclaration actual = Parser.ParseFromString(input);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void TestNumericDeclaration()
        {
            string input = """
                Ammo_RocketAir_Hydra_APKWS_x7 is TAmmunitionDescriptor
                (
                    MaximumRangeGRU                 = 4550
                )
                """;

            FileDeclaration expected = new FileDeclaration([new AssignDeclaration(false, "Ammo_RocketAir_Hydra_APKWS_x7", new ObjectValue
                ( "TAmmunitionDescriptor",
                    [ ("MaximumRangeGRU", new NumericLiteral("4550"))
                    ]
                ))]);

            FileDeclaration actual = Parser.ParseFromString(input);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void TestNestedObjectDeclaration()
        {
            string input = """
                Ammo_RocketAir_Hydra_APKWS_x7 is TAmmunitionDescriptor
                (
                    HitRollRuleDescriptor = TDiceHitRollRuleDescriptor
                    (

                    )
                )
                """;

            FileDeclaration expected = new FileDeclaration(
                [new AssignDeclaration(false, "Ammo_RocketAir_Hydra_APKWS_x7", new ObjectValue( "TAmmunitionDescriptor",
                    [   
                        ("HitRollRuleDescriptor", new ObjectValue("TDiceHitRollRuleDescriptor", 
                        [
                            
                        ]))
                    ]
                ))]);

            FileDeclaration actual = Parser.ParseFromString(input);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void TestPairValueDeclaration()
        {
            string input = """
                Ammo_RocketAir_Hydra_APKWS_x7 is TAmmunitionDescriptor
                (
                    BaseHitValueModifiers =
                    [
                        (EBaseHitValueModifier/Base, 0.0),
                    ]
                )
                """;

            FileDeclaration expected = new FileDeclaration(
                [new AssignDeclaration(false, "Ammo_RocketAir_Hydra_APKWS_x7", new ObjectValue( "TAmmunitionDescriptor",
                    [ 
                        ("BaseHitValueModifiers", new ArrayValue(
                        [
                            new PairValue(new PathValue("EBaseHitValueModifier/Base"), new NumericLiteral("0.0"))
                        ]))                       
                    ]
                ))]);

            FileDeclaration actual = Parser.ParseFromString(input);

            Assert.Equal(expected, actual);
        }
        [Fact]
        public void TestAbsReferenceDeclaration()
        {
            string input = """
                Ammo_RocketAir_Hydra_APKWS_x7 is TAmmunitionDescriptor
                (
                    FireDescriptor                    = $/GFX/Weapon/Descriptor_Fire_Incendie
                )
                """;

            FileDeclaration expected = new FileDeclaration([new AssignDeclaration(false, "Ammo_RocketAir_Hydra_APKWS_x7", new ObjectValue
                ( "TAmmunitionDescriptor",
                    [ ("FireDescriptor", new AbsReference("$/GFX/Weapon/Descriptor_Fire_Incendie"))
                    ]
                ))]);

            FileDeclaration actual = Parser.ParseFromString(input);

            Assert.Equal(expected, actual);
        }

        [Fact]
        public void TestRelReferenceDeclaration()
        {
            string input = """
                export WeaponDescriptor_2K11_KRUG_DDR is TWeaponManagerModuleDescriptor
                (
                    TurretIdleBehaviourDescriptor = ~/TurretIdle_DCAAutoMoteur
                )
                """;

            FileDeclaration expected = new FileDeclaration([new AssignDeclaration(true, "WeaponDescriptor_2K11_KRUG_DDR", new ObjectValue
                ( "TWeaponManagerModuleDescriptor",
                    [ ("TurretIdleBehaviourDescriptor", new RelReference("~/TurretIdle_DCAAutoMoteur"))
                    ]
                ))]);

            FileDeclaration actual = Parser.ParseFromString(input);

            Assert.Equal(expected, actual);
        }
    }
}