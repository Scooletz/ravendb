import * as yup from "yup";

export type SetupWizardSetupMethod = "newCluster" | "createPackage" | "usePackage";

const setupMethodStepSchema = yup.object({
    method: yup.string<SetupWizardSetupMethod>().nullable().required(),
});

const usePackageStepSchema = yup.object({});

const licenseKeyStepSchema = yup.object({});

const securityStepSchema = yup.object({});

const selfSignedCertificateStepSchema = yup.object({});

const domainStepSchema = yup.object({});

const nodeAddressStepSchema = yup.object({});

const additionalSettingsStepSchema = yup.object({});

export type SetupWizardStepId =
    | "Eula"
    | "Setup method"
    | "Use setup package"
    | "License key"
    | "Security"
    | "Self-signed certificate"
    | "Domain"
    | "Node address"
    | "Additional settings"
    | "Summary"
    | "Finish";

export const setupWizardSchema = yup.object({
    currentStep: yup.string<SetupWizardStepId>(),
    setupMethodStep: setupMethodStepSchema,
    usePackageStep: usePackageStepSchema,
    licenseKeyStep: licenseKeyStepSchema,
    securityStep: securityStepSchema,
    selfSignedCertificateStep: selfSignedCertificateStepSchema,
    domainStep: domainStepSchema,
    nodeAddressStep: nodeAddressStepSchema,
    additionalSettingsStep: additionalSettingsStepSchema,
});

export type SetupWizardFormData = yup.InferType<typeof setupWizardSchema>;
