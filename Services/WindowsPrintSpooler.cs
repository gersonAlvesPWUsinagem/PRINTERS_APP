using System.IO;
using System.Net.Sockets;

namespace PRINTERS_APP.Services;

public interface IPrintSpooler
{
    Task EnviarAsync(
        string nomeImpressora,
        string enderecoImpressora,
        int porta,
        string nomeArquivo,
        byte[] dadosArquivo,
        CancellationToken cancellationToken);
}

/// <summary>
/// Envia um trabalho já renderizado (PCL/PS) diretamente para a porta RAW da
/// impressora. Isso evita que um driver V4 tente processar novamente o PRN.
/// </summary>
public sealed class WindowsPrintSpooler : IPrintSpooler
{
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(15);

    public async Task EnviarAsync(
        string nomeImpressora,
        string enderecoImpressora,
        int porta,
        string nomeArquivo,
        byte[] dadosArquivo,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nomeImpressora);
        ArgumentException.ThrowIfNullOrWhiteSpace(enderecoImpressora);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(porta);
        ArgumentException.ThrowIfNullOrWhiteSpace(nomeArquivo);
        ArgumentNullException.ThrowIfNull(dadosArquivo);

        if (dadosArquivo.Length == 0)
            throw new ArgumentException("O documento de impressão está vazio.", nameof(dadosArquivo));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ConnectionTimeout);

        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(enderecoImpressora, porta, timeout.Token);
            await using var stream = client.GetStream();
            await stream.WriteAsync(dadosArquivo, timeout.Token);
            await stream.FlushAsync(timeout.Token);

            // O fechamento do envio sinaliza o fim do trabalho para a porta RAW.
            client.Client.Shutdown(SocketShutdown.Send);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new IOException(
                $"Tempo esgotado ao enviar '{nomeArquivo}' para " +
                $"'{nomeImpressora}' ({enderecoImpressora}:{porta}).");
        }
        catch (SocketException ex)
        {
            throw new IOException(
                $"Falha de rede ao enviar '{nomeArquivo}' para " +
                $"'{nomeImpressora}' ({enderecoImpressora}:{porta}).",
                ex);
        }
    }
}
