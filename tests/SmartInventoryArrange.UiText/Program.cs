using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Reflection.Emit;
string file=Path.GetFullPath(args.Length>0?args[0]:"src/SmartInventoryArrange/bin/Release/net6.0/SmartInventoryArrange.dll");
using var stream=File.OpenRead(file);using var pe=new PEReader(stream);var reader=pe.GetMetadataReader();
var type=reader.TypeDefinitions.Select(reader.GetTypeDefinition).Single(t=>reader.GetString(t.Namespace)=="SmartInventoryArrange"&&reader.GetString(t.Name)=="NativeSmartMenu");
var method=type.GetMethods().Select(reader.GetMethodDefinition).Single(m=>reader.GetString(m.Name)=="Open");var il=pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!;
var opcodes=typeof(OpCodes).GetFields(System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public).Where(f=>f.FieldType==typeof(OpCode)).Select(f=>(OpCode)f.GetValue(null)!).ToDictionary(o=>(ushort)o.Value);var strings=new List<string>();int at=0;
while(at<il.Length){ushort code=il[at++];if(code==0xFE)code=(ushort)(0xFE00|il[at++]);var op=opcodes[code];if(op.OperandType==OperandType.InlineString){int token=BitConverter.ToInt32(il,at);strings.Add(reader.GetUserString(MetadataTokens.UserStringHandle(token&0xFFFFFF)));}
int size=op.OperandType switch{OperandType.InlineNone=>0,OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar=>1,OperandType.InlineVar=>2,OperandType.InlineI8 or OperandType.InlineR=>8,OperandType.InlineSwitch=>4+4*BitConverter.ToInt32(il,at),_=>4};at+=size;}
string chinese="\u667A\u80FD\u6392\u5217";
bool exact=strings.Count(s=>s.Any(c=>c>127))==2&&strings.Contains(chinese)&&strings.Contains("\u4FDD\u7559\u5BB9\u5668\u6392\u5217"),english=strings.Contains("Smart Arrange")&&strings.Contains("Preserve Containers");var version=reader.GetAssemblyDefinition().Version;
if(!exact||!english||version.ToString()!="3.17.9.0")throw new Exception("UI text/version mismatch");Console.WriteLine($"UI_TEXT version={version} chineseExact=True englishExact=True exit=0");
