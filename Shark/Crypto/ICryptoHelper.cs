namespace Shark.Crypto
{
    public interface ICryptoHelper
    {
        byte[] EncryptSingleBlock(byte[] inputBuffer, int offset, int count);

        byte[] DecryptSingleBlock(byte[] inputBuffer, int offset, int count);
    }
}
