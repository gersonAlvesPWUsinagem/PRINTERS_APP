using System.Text.Json.Serialization;

namespace PRINTERS_APP.Helpers
{
    /// <summary>
    /// Classe responsável por encapsular dados de resposta de uma API, incluindo mensagens de erro e status de erro.
    /// </summary>
    /// <typeparam name="T">O tipo de dado encapsulado pela resposta da API.</typeparam>
    public class ApiDataService<T>
    {
        /// <summary>
        /// Obtém ou define os dados da resposta da API.
        /// </summary>
        [JsonPropertyName("Value")]
        public T? Value { get; set; }

        /// <summary>
        /// Obtém ou define a mensagem de erro associada à resposta da API.
        /// </summary>
        [JsonPropertyName("Message")]
        public string? Message { get; set; }

        /// <summary>
        /// Obtém ou define um valor que indica se ocorreu um erro na resposta da API.
        /// </summary>
        [JsonPropertyName("Success")]
        public bool Success { get; set; }


        [JsonPropertyName("StatusCode")]
        public int StatusCode { get; set; }

        /// <summary>
        /// Inicializa uma nova instância da classe <see cref="ApiDataService{T}"/>.
        /// </summary>
        public ApiDataService() { }

        /// <summary>
        /// Inicializa uma nova instância da classe <see cref="ApiDataService{T}"/> com os dados, status de erro e mensagem fornecidos.
        /// </summary>
        /// <param name="data">Os dados da resposta da API.</param>
        /// <param name="isError">Indica se ocorreu um erro na resposta da API.</param>
        /// <param name="message">A mensagem de erro associada à resposta da API.</param>
        public ApiDataService(T value, bool success, string message, int statusCode)
        {
            Value = value == null ? EnsureDataInstance(value) : value;
            Message = message.Equals(string.IsNullOrEmpty(message)) ? string.Empty : message;
            Success = success;
            StatusCode = statusCode;
        }

        /// <summary>
        /// Garante que a propriedade <see cref="Value"/> seja instanciada se aplicável.
        /// </summary>
        /// <param name="data">Os dados da resposta da API.</param>
        /// <returns>Retorna uma nova instância do tipo <typeparamref name="T"/>.</returns>
        private T EnsureDataInstance(T data) => Activator.CreateInstance<T>();
    }
}
