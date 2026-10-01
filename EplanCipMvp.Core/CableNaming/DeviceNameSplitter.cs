namespace EplanCipMvp.Core.CableNaming
{
    /// <summary>
    /// Раскладка имени кабеля на части NameParts EPLAN: FUNC_CODE = ведущие буквы,
    /// FUNC_COUNTER = остаток. Гипотеза (не подтверждена живым тестом) — при чтении
    /// кабелей App логирует реальную раскладку существующих имён для сверки.
    /// </summary>
    public static class DeviceNameSplitter
    {
        public static (string Code, string Counter) Split(string name)
        {
            name = name ?? "";
            int i = 0;
            while (i < name.Length && char.IsLetter(name[i])) i++;
            return (name.Substring(0, i), name.Substring(i));
        }
    }
}
