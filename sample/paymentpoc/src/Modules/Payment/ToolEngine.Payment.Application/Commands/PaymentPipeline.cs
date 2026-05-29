namespace ToolEngine.Payment.Application.Commands;

/// <summary>Tool names and namespace for all payment pipeline stages.</summary>
internal static class PaymentPipeline
{
    public const string Namespace = "payment";
    public const string Version   = "v1";

    public static class Stage
    {
        public const string Initiate       = "initiate";
        public const string VerifyPayee    = "verify-payee";
        public const string PpmCheck       = "ppm-check";
        public const string CalculateWht   = "calculate-wht";
        public const string KycScreen      = "kyc-screen";
        public const string CompileDossier = "compile-dossier";
        public const string ExecutePayment = "execute-payment";
        public const string Reconcile      = "reconcile";
    }
}
