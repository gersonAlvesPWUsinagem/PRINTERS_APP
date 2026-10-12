namespace PRINTERS_APP.Security;

public static class PasswordCipher
{
    public static string Crypt(string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        var characters = text.ToCharArray();
        for (var index = 0; index < characters.Length; index++)
        {
            var value = characters[index];
            characters[index] = (char)(value < 128 ? value + 128 : value - 128);
        }
        return new string(characters);
    }

    // Compatibilidade com senhas legadas gravadas pela aplicação ADI.
    // Este algoritmo ainda existe no contrato compartilhado como EncriptarSenha.
    public static string EncryptLegacy(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        text = text.Trim();
        var encrypted = new char[text.Length];
        for (var index = 0; index < text.Length; index++)
            encrypted[index] = (char)(text[index] + index + 2);

        return new string(encrypted);
    }
}
