import { SetupWizardFormData } from "components/setupWizard/setupWizardValidation";

export function getLicenseType(licenseInfo: SetupWizardFormData["licenseKeyStep"]["licenseInfo"]) {
    const type = licenseInfo?.licenseType || "None";

    const licenseTiers: Record<Raven.Server.Commercial.LicenseType, number> = {
        None: 0,
        Invalid: 0,
        Reserved: 0,
        Community: 1,
        Essential: 1,
        Professional: 3,
        Enterprise: 4,
        EnterpriseAi: 4,
        Developer: 4, // Enterprise-level set of features
    };

    return {
        type,
        isAtLeast: (minimumType: Raven.Server.Commercial.LicenseType) =>
            (licenseTiers[type] || 0) >= (licenseTiers[minimumType] || 0),
        isHigherThan: (compareType: Raven.Server.Commercial.LicenseType) =>
            (licenseTiers[type] || 0) > (licenseTiers[compareType] || 0),
        isCommunity: () => type === "Community" || type === "Essential", // I think community and essential are the same
        isDeveloper: () => type === "Developer",
        isProfessionalOrHigher: () => (licenseTiers[type] || 0) >= licenseTiers["Professional"],
        isEnterprise: () => type === "Enterprise",
        hasLicense: () => type !== "None" && type !== "Invalid",
    };
}
