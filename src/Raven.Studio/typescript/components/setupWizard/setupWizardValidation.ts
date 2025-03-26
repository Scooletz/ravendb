import * as yup from "yup";
import { setupWizardConstants } from "./utils/setupWizardConstants";
import { ipAddressFormSchema } from "components/setupWizard/steps/SetupWizardNodeAddressStep";

export type SetupWizardSetupMethod = "newCluster" | "createPackage" | "usePackage";
export type SetupWizardSecurityOption = "letsEncrypt" | "ownCertificate" | "none";
export type LicenseTypeToGenerate = "community" | "developer";

const setupMethodStepSchema = yup.object({
    method: yup.string<SetupWizardSetupMethod>().nullable().required(),
});

const usePackageStepSchema = yup.object({
    fileName: yup.string(), // TODO is it needed?
    fileZip: yup.string(), // should be required
    isZipSecure: yup.boolean(),
    nodeTag: yup.string(), // should be required
});

function licenseRequiredField(schema: yup.Schema) {
    return schema.when("licenseTypeToGenerate", {
        is: (licenseTypeToGenerate: LicenseTypeToGenerate) => licenseTypeToGenerate != null,
        then: (schema) => schema.required(),
    });
}

const licenseKeyStepSchema = yup.object({
    key: yup.string(),
    licenseInfo: yup.object({
        licenseType: yup.string<Raven.Server.Commercial.LicenseType>(),
        userDomainsWithIps: yup.object({
            email: yup.array().of(yup.string()),
            rootDomains: yup.array().of(yup.string()),
            domains: yup.object({
                // TODO add dynamic keys - use yup.lazy() to construct an object schema based on the input value
                // [yup.string().required()]: yup.array().of(yup.string()),
            }),
        }),
        maxClusterSize: yup.number(),
    }),
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

const selfSignedCertificateStepSchema = yup.object({
    certificateFileName: yup.string(), // TODO is it needed?
    certificate: yup.string(),
    password: yup.string(),
    cns: yup.array().of(yup.string()),
});

const domainStepSchema = yup.object({
    domain: yup.string(),
    email: yup.string().email(),
    rootDomain: yup.string(),
});

const nodeAddressStepSchema = yup.object({
    nodes: yup.array().of(
        yup.object({
            nodeTag: yup.string(),
            nodeUrl: yup.string(),
            httpPort: yup.number(),
            tcpPort: yup.number(),
            ipAddress: yup.array().of(ipAddressFormSchema),
            externalIpAddress: yup.string(),
            externalHttpPort: yup.number(),
            externalTcpPort: yup.number(),
            dnsName: yup.string(),
            isPassive: yup.boolean(),

            // states
            isNewlyAdded: yup.boolean(),
            isEditing: yup.boolean(),
            hasExternalConfig: yup.boolean(),
        })
    ),
});

const additionalSettingsStepSchema = yup.object({
    serverEnvironment: yup.string().oneOf(setupWizardConstants.allServerEnvironments),
    adminCertificateExpirationTime: yup.number(),
    dataDirectory: yup.string().nullable(),
    setupCertificatePath: yup.string().nullable(),
    postgresqlIntegration: yup.boolean(),

    // states
    isAdvancedSettingsVisible: yup.boolean(),
});

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
