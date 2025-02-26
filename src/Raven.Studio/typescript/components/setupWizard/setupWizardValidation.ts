import * as yup from "yup";

const setupMethodStepSchema = yup.object({});

const usePackageStepSchema = yup.object({});

const licenseKeyStepSchema = yup.object({});

const securityStepSchema = yup.object({});

const selfSignedCertificateStepSchema = yup.object({});

const domainStepSchema = yup.object({});

const nodeAddressStepSchema = yup.object({});

const additionalSettingsStepSchema = yup.object({});

export const setupWizardSchema = yup.object({
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
