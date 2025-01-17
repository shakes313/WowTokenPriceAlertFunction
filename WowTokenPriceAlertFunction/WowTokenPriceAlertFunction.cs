using Azure.Communication.Sms;
using Microsoft.Azure.WebJobs;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Mail;
using System.Text;
using System.Threading.Tasks;
using Azure.Storage.Blobs;

namespace TokenPriceAlertFunctionApp
{
    public class WowTokenPriceAlertFunction
    {
        private ILogger _logger;
        private readonly string tokenWebPage = "https://wowauction.us/token";

        // Use the same storage for the prior token value as that which is used for the function itself
        private readonly string blobConnectionString = Environment.GetEnvironmentVariable("AzureWebJobsStorage");
        private readonly string containerName = "state-container";
        private readonly string blobName = "state.txt";

        // Use this variable for the min amount to be alerted for (e.g. set to 200000 to be alerted when the price hits or goes under that value)
        private readonly int tokenAmountAlertMinimum = int.Parse(Environment.GetEnvironmentVariable("TOKEN_AMOUNT_ALERT_LOW"));

        // Use this variable for the max amount to be alerted for (e.g. set to 400000 to be alerted when the price hits or goes over that value)
        private readonly int tokenAmountAlertMaximum = int.Parse(Environment.GetEnvironmentVariable("TOKEN_AMOUNT_ALERT_HIGH"));

        // When attempting to get the current token price, use the retries and delay values to control how many times, and the delay between each
        //  to make the check when there is an issue
        private readonly int maxRetries = int.Parse(Environment.GetEnvironmentVariable("GET_TOKEN_CALL_MAX_TRIES"));
        private readonly int retryDelayMilliseconds = int.Parse(Environment.GetEnvironmentVariable("GET_TOKEN_CALL_RETRY_DELAY_MS"));

        // Set these to you gmail address and the the app password generated in your gmail account (if using 2FA)
        private readonly string fromEmail = Environment.GetEnvironmentVariable("FROM_EMAIL");
        private readonly string fromPassword = Environment.GetEnvironmentVariable("FROM_EMAIL_PASSWORD");

        // The email to send to when the amount goes over/under the min/max
        private readonly string toAddresses = Environment.GetEnvironmentVariable("ON_ALERT_EMAIL_TO");

        // Email address to send to whenever the value changes, regardless of the min/max
        private readonly string toAddressesAlways = Environment.GetEnvironmentVariable("ALWAYS_NOTIFY_EMAIL_TO");

        // Creds for Windows Communication service. If not used, just add the variables and leave blank
        private readonly string acsConnectionString = Environment.GetEnvironmentVariable("WCS_CONNECTION_STRING");
        private readonly string acsPhoneNumber = Environment.GetEnvironmentVariable("WCS_PHONE_NUMBER");

        private readonly bool storeLastPrice = Environment.GetEnvironmentVariable("STORE_LAST_PRICE") == "Y";

        [FunctionName("WowTokenPriceAlertFunction")]
        public async Task Run([TimerTrigger("15 */5 * * * *", UseMonitor = false)] TimerInfo myTimer, ILogger log)
        {
            _logger = log;

            try
            {
                var tokenAmount = await GetTokenAmountAsync();
                if (tokenAmount == -1)
                    return;

                var lastTokenPrice = storeLastPrice ? await GetLastTokenPriceAsync() : -1;
                if (lastTokenPrice == tokenAmount)
                    return;

                if (storeLastPrice)
                    await SaveTokenPriceAsync(tokenAmount);

                var diff = tokenAmount - lastTokenPrice;
                var diffString = lastTokenPrice <= 0 ? "" : $" [{(diff > 0 ? "+" : "")}{diff:n0}]";

                if (!string.IsNullOrEmpty(toAddressesAlways))
                {
                    _logger.LogInformation($"Sending Email/Text(s) to {toAddressesAlways}. Token price: {tokenAmount:n0}{diffString}");
                    foreach (var to in toAddressesAlways.Split(';'))
                    {
                        await SendMessageAsync(to, $"WoW Token: {tokenAmount:n0}{diffString}", "-x-x-x-x-x-x-x-x-x-x-x-x-x-x-"); // $"WoW token price is currently {tokenAmount:n0}");
                    }
                }

                if ((tokenAmountAlertMinimum != -1 && tokenAmount <= tokenAmountAlertMinimum)
                    || (tokenAmountAlertMaximum != -1 && tokenAmount >= tokenAmountAlertMaximum))
                {
                    _logger.LogInformation($"Sending Email/Text(s) to {toAddresses}. Token price: {tokenAmount:n0}{diffString}");
                    foreach (var to in toAddresses.Split(';'))
                    {
                        await SendMessageAsync(to, $"WoW Token: {tokenAmount:n0}{diffString}", "-x-x-x-x-x-x-x-x-x-x-x-x-x-x-"); // $"WoW token price is currently {tokenAmount:n0}");
                    }
                }
                else
                {
                    _logger.LogInformation($"Current Token Price: {tokenAmount:n0}{diffString}. LOW: {tokenAmountAlertMinimum:n0}. HIGH: {tokenAmountAlertMaximum:n0}");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Run Error: {ex.Message}");
            }
        }

        private async Task<int> GetTokenAmountAsync(int retryCount = 0)
        {
            string errorMessage = "";
            try
            {
                using HttpClient client = new HttpClient();
                var html = await client.GetStringAsync(tokenWebPage);
                var startIndex = html.IndexOf("<b>Current:</b>");

                if (startIndex <= 0)
                {
                    errorMessage = "Token element not found in web page (startIndex <= 0).";
                }
                else
                {
                    var htmlTokenValue = html.Substring(startIndex, 50).Replace("<b>Current:</b>&nbsp;", "");
                    htmlTokenValue = htmlTokenValue[..htmlTokenValue.IndexOf("&nbsp;")];
                    if (int.TryParse(htmlTokenValue?.Replace(",", ""), out int amount))
                    {
                        return amount;
                    }

                    errorMessage = "Token element not found in web page (string in html content not a valid integer).";
                }
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
            }

            if (!string.IsNullOrEmpty(errorMessage))
            {
                _logger.LogError($"GetTokenAmount Error (try {retryCount} of {maxRetries}): {errorMessage}");
            }

            if (retryCount <= maxRetries)
            {
                await Task.Delay(retryDelayMilliseconds);
                return await GetTokenAmountAsync(++retryCount);
            }

            return -1;
        }

        private async Task SendMessageAsync(string toAddress, string subject, string body)
        {
            if (toAddress.Contains('@'))
            {
                await SendEmailAsync(toAddress, subject, body);
            }
            else
            {
                await SendSmsAsync(toAddress, subject);
            }
        }

        private async Task SendEmailAsync(string toAddress, string subject, string body)
        {
            try
            {
                using MailMessage mail = new()
                {
                    From = new MailAddress(fromEmail),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = false
                };

                mail.To.Add(toAddress);

                using SmtpClient smtp = new("smtp.gmail.com", 587)
                {
                    Credentials = new System.Net.NetworkCredential(fromEmail, fromPassword),
                    EnableSsl = true
                };

                await smtp.SendMailAsync(mail);
            }
            catch (Exception ex)
            {
                _logger?.LogError($"SendEmailAsync Error: {ex.Message}");
            }
        }

        public async Task SendSmsAsync(string toPhoneNumber, string messageBody)
        {
            if (string.IsNullOrEmpty(acsConnectionString) || string.IsNullOrEmpty(acsPhoneNumber))
                return;

            try
            {
                var smsClient = new SmsClient(acsConnectionString);

                var response = await smsClient.SendAsync(
                    from: acsPhoneNumber,
                    to: toPhoneNumber,
                    message: messageBody
                );
            }
            catch (Exception ex)
            {
                _logger?.LogError($"SendSmsAsync Error: {ex.Message}");
            }
        }

        async Task SaveTokenPriceAsync(int value)
        {
            try
            {
                var container = new BlobContainerClient(blobConnectionString, containerName);
                await container.CreateIfNotExistsAsync();
                var blob = container.GetBlobClient(blobName);
                await blob.UploadAsync(new MemoryStream(Encoding.UTF8.GetBytes(value.ToString())), overwrite: true);
            }
            catch (Exception ex)
            {
                _logger?.LogError($"SaveTokenPriceAsync Error: {ex.Message}");
            }
        }

        async Task<int> GetLastTokenPriceAsync()
        {
            try
            {
                var container = new BlobContainerClient(blobConnectionString, containerName);
                var blob = container.GetBlobClient(blobName);

                if (await blob.ExistsAsync())
                {
                    var response = await blob.DownloadContentAsync();
                    return int.Parse(response.Value.Content.ToString());
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"GetLastTokenPriceAsync Error: {ex.Message}");
            }

            return -1;
        }
    }
}