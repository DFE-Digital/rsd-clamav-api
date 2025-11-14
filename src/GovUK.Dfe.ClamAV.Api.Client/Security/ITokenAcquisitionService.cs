namespace GovUK.Dfe.ClamAV.Api.Client.Security
{
    public interface ITokenAcquisitionService
    {
        Task<string> GetTokenAsync(CancellationToken cancellationToken);
    }
}
