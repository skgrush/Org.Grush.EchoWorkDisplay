using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Azure.Identity;
using Azure.Identity.Broker;
using Microsoft.Graph;
using Microsoft.Graph.Models;

namespace Org.Grush.EchoWorkDisplay;

public partial class MicrosoftPresenceService(Config config, HashAlgorithm secureHash)
{
    public enum Availability
    {
        Presenting,
        DoNotDisturb,
        Focusing,
        InACall,
        InAMeeting,
        Busy,
        Offline,
        BeRightBack,
        Away,
        Available,
        PresenceUnknown
    }
    
    public record PresenceDescription(
        DateTimeOffset Timestamp,
        string? Error = null,
        string? SequenceNumber = null,
        string? OutOfOfficeMessage = null,
        string? StatusMessage = null,
        Availability? Availability = null
    )
    {
        public const string DefaultOutOfOfficeMessage = "Out of Office";
        
        public static PresenceDescription FromError(string error)
            => new(DateTimeOffset.Now, error);

        public static PresenceDescription FromPresence(Microsoft.Graph.Models.Presence presence)
            => new(
                DateTimeOffset.Now,
                SequenceNumber: presence.SequenceNumber,
                OutOfOfficeMessage: presence.OutOfOfficeSettings is { IsOutOfOffice: true }
                    ? presence.OutOfOfficeSettings?.Message ?? DefaultOutOfOfficeMessage
                    : null,
                StatusMessage: presence.StatusMessage?.Message is { ContentType: BodyType.Text, Content: {} content }
                    ? content
                    : null,
                Availability: ParseAvailability(presence.Availability)
            );

        public static Availability? ParseAvailability(string? availability)
        {
            if (availability is null)
                return null;

            return Enum.TryParse(availability, ignoreCase: true, out Availability availabilityEnum)
                ? availabilityEnum
                : MicrosoftPresenceService.Availability.PresenceUnknown;
        }
    }

    private PresenceDescription Presence
    {
        get;
        set
        {
            PresenceChanged?.Invoke(this, value);
            field = value;
        }
    } = PresenceDescription.FromError("Not initialized");
    
    public event EventHandler<PresenceDescription>? PresenceChanged;
    
    private (GraphServiceClient Client, string CredentialsHash)? GraphClient { get; set; }

    public readonly IEnumerable<string> GraphScopes = ["Presence.Read"];

    public async void LoopAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                // TODO: re-read config
                await CheckPresenceAsync(cancellationToken);

                await Task.Delay(
                    Presence.Error is null
                        ? config.AzRefreshPeriodMilliseconds
                        : 10 * config.AzRefreshPeriodMilliseconds,
                    cancellationToken
                );
            }
            catch (TaskCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                Console.WriteLine("Error looping presence: {0}", e);
                await Task.Delay(10 * config.AzRefreshPeriodMilliseconds, cancellationToken);
            }
        }
    }

    public async Task CheckPresenceAsync(CancellationToken cancellationToken)
    {
        GraphServiceClient client;
        try
        {
            var c = await AuthAsync(cancellationToken);

            if (c is null)
            {
                Presence = PresenceDescription.FromError("Not authenticated");
                return;
            }
            client = c;
        }
        catch (Exception e) when (e is not TaskCanceledException)
        {
            Presence = PresenceDescription.FromError($"Not authenticated: {e}");
            return;
        }

        try
        {
            var presenceDto = await client.Me.Presence.GetAsync(
                cancellationToken: cancellationToken
            );

            if (presenceDto?.SequenceNumber is null)
            {
                Presence = PresenceDescription.FromError("API failure: Unknown");
                return;
            }
            
            Presence = PresenceDescription.FromPresence(presenceDto);
            return;
        }
        catch (Exception e) when (e is not TaskCanceledException)
        {
            Presence = PresenceDescription.FromError($"API failure: {e}");
            return;
        }
    }

    private async Task<GraphServiceClient?> AuthAsync(CancellationToken cancellationToken)
    {
        var credsHash = HashStrings(config.AzClientId, config.AzTenantId);
        if (GraphClient?.CredentialsHash != credsHash)
        {
            if (GraphClient is not null)
            {
                GraphClient.Value.Client.Dispose();
                GraphClient = null;
            }
            
            if (config.AzClientId is null or "" || config.AzTenantId is null or "")
            {
                return null;
            }
            
            IntPtr parentWindowHandle = GetForegroundWindow();
            
            InteractiveBrowserCredentialBrokerOptions options = new(parentWindowHandle)
            {
                ClientId = config.AzClientId,
                TenantId =  config.AzTenantId,
            };
            
            InteractiveBrowserCredential credential = new(options);

            GraphClient = (
                new GraphServiceClient(credential, GraphScopes),
                credsHash
            );
        }

        var client = GraphClient.Value.Client;

        return client;
    }
    
    [LibraryImport("user32.dll", EntryPoint = "GetForegroundWindowA")]
    private static partial IntPtr GetForegroundWindow();

    private string HashStrings(params IEnumerable<string> p)
        => Convert.ToBase64String(
            secureHash.ComputeHash(
                Encoding.UTF8.GetBytes(
                    string.Join('\0', p)
                )
            )
        );
}