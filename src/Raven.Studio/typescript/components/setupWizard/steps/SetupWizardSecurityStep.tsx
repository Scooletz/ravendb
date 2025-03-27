import { useFormContext, useWatch } from "react-hook-form";
import { SetupWizardFormData } from "../setupWizardValidation";
import { Icon } from "components/common/Icon";
import SetupWizardClickableCard from "../partials/SetupWizardClickableCard";
import Button from "react-bootstrap/Button";
import { FormCheckbox } from "components/common/Form";
import assertUnreachable from "components/utils/assertUnreachable";
import Badge from "react-bootstrap/Badge";
import { useAsync } from "react-async-hook";
import { useServices } from "hooks/useServices";
import Spinner from "react-bootstrap/Spinner";

export function SetupWizardSecurityStep() {
    const { control, setValue } = useFormContext<SetupWizardFormData>();

    const {
        securityStep: { securityOption },
        licenseKeyStep: { key, licenseInfo },
    } = useWatch({ control });

    // TODO Add conditional recommended badge to Secure

    const isSecureDisabled = !key;
    const isSecureRecommended = !!key && licenseInfo.licenseType !== "Developer";

    return (
        <div>
            <h2>Security</h2>
            <p>Select the security option that best addresses your needs</p>
            <div className="mt-4">
                <h5 className="mb-1">
                    <Icon icon="lock" color="success" />
                    Secure
                    {isSecureRecommended && (
                        <Badge pill bg="success" className="ms-1">
                            Recommended
                        </Badge>
                    )}
                </h5>
                <SetupWizardClickableCard
                    icon="lets-encrypt"
                    title="Generate Let’s Encrypt certificate"
                    description="Secure and hassle-free communication with automatic certificate management"
                    isSelected={securityOption === "letsEncrypt"}
                    onClick={() => setValue("securityStep.securityOption", "letsEncrypt")}
                    isDisabled={isSecureDisabled}
                />
                <SetupWizardClickableCard
                    className="mt-2"
                    icon="certificate"
                    title="Provide your own certificate"
                    description="Ideal for secure corporate setups with manual certificate management"
                    isSelected={securityOption === "ownCertificate"}
                    onClick={() => setValue("securityStep.securityOption", "ownCertificate")}
                    isDisabled={isSecureDisabled}
                />
            </div>
            <div className="mt-4">
                <h5 className="mb-1">
                    <Icon icon="lock" color="warning" />
                    Unsecure
                </h5>
                <SetupWizardClickableCard
                    icon="empty-set"
                    title="Don’t use certificate"
                    description="Best for quick local development with no security requirements"
                    isSelected={securityOption === "none"}
                    onClick={() => setValue("securityStep.securityOption", "none")}
                />
            </div>
        </div>
    );
}

export function SetupWizardSecurityStepFooter() {
    const { control, setValue } = useFormContext<SetupWizardFormData>();
    const { setupWizardService } = useServices();

    const {
        securityStep: { securityOption, isLetsEncryptAgreementAccepted },
        licenseKeyStep: { licenseInfo },
    } = useWatch({ control });

    const asyncGetLetsEncryptAgreement = useAsync(
        () => setupWizardService.getLetsEncryptAgreement(licenseInfo.userDomainsWithIps.email[0] ?? ""),
        []
    ); // TODO check if there could be more than one email assigned to license

    const handleBack = () => {
        setValue("currentStep", "License key");
    };

    const handleContinue = () => {
        switch (securityOption) {
            case "letsEncrypt":
                setValue("currentStep", "Domain");
                break;
            case "ownCertificate":
                setValue("currentStep", "Self-signed certificate");
                break;
            case "none":
                setValue("currentStep", "Node address");
                break;
            default:
                assertUnreachable(securityOption);
        }
    };

    return (
        <div className="hstack justify-content-between">
            <Button variant="secondary" className="rounded-pill" onClick={handleBack}>
                <Icon icon="arrow-left" /> Back
            </Button>
            <div className="hstack gap-2 align-items-center">
                {securityOption === "letsEncrypt" && (
                    <FormCheckbox
                        disabled={asyncGetLetsEncryptAgreement.loading}
                        className="mb-0"
                        control={control}
                        name="securityStep.isLetsEncryptAgreementAccepted"
                    >
                        I accept{" "}
                        {asyncGetLetsEncryptAgreement.loading && <Spinner />}
                        <a target="_blank" href={asyncGetLetsEncryptAgreement.result as string}>
                            Let&apos;s Encrypt Subscriber Agreement
                        </a>
                    </FormCheckbox>
                )}
                <Button disabled={!isLetsEncryptAgreementAccepted && securityOption === "letsEncrypt"} variant="primary" className="rounded-pill" onClick={handleContinue}>
                    Continue <Icon icon="arrow-right" margin="m-0" />
                </Button>
            </div>
        </div>
    );
}
