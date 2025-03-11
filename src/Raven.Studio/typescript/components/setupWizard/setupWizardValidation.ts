import * as yup from "yup";
import { setupWizardConstants } from "./partials/SetupWizardConstants";

export type SetupWizardSetupMethod = "newCluster" | "createPackage" | "usePackage";
export type SetupWizardSecurityOption = "letsEncrypt" | "ownCertificate" | "none";
export type LicenseTypeToGenerate = "community" | "developer";

const setupMethodStepSchema = yup.object({
    method: yup.string<SetupWizardSetupMethod>().nullable().required(),
});

const usePackageStepSchema = yup.object({});

function licenseRequiredField(schema: yup.Schema) {
    return schema.when("licenseTypeToGenerate", {
        is: (licenseTypeToGenerate: LicenseTypeToGenerate) => licenseTypeToGenerate != null,
        then: (schema) => schema.required(),
    });
}

const licenseKeyStepSchema = yup.object({
    key: yup.string(),
    licenseTypeToGenerate: yup.string<LicenseTypeToGenerate>().nullable(),
    isAcceptTerms: yup.boolean(),
    isAcceptEmails: yup.boolean(),
    firstName: licenseRequiredField(yup.string()),
    lastName: licenseRequiredField(yup.string()),
    email: licenseRequiredField(yup.string()), // TODO email validation
    phone: yup.string(), // TODO phone validation
    country: licenseRequiredField(yup.string().oneOf(setupWizardConstants.allCountries)),
    jobTitle: licenseRequiredField(yup.string().oneOf(setupWizardConstants.allJobTitles)),
    industry: licenseRequiredField(yup.string().oneOf(setupWizardConstants.allIndustries)),
    company: licenseRequiredField(yup.string()),
    howYouPlanToUseRavenDB: licenseRequiredField(yup.string().oneOf(setupWizardConstants.allHowYouPlanToUseRavenDB)),
});

const securityStepSchema = yup.object({
    securityOption: yup.string<SetupWizardSecurityOption>().nullable().required(),
    isLetsEncryptAgreementAccepted: yup.boolean(),
});

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
