import { useFormContext, useWatch } from "react-hook-form";
import { SetupWizardFormData } from "../setupWizardValidation";
import { Icon } from "components/common/Icon";
import SetupWizardClickableCard from "../partials/SetupWizardClickableCard";
import { Button } from "react-bootstrap";
import { FormCheckbox } from "components/common/Form";
import assertUnreachable from "components/utils/assertUnreachable";

export default function SetupWizardSecurityStep() {
    const { control, setValue } = useFormContext<SetupWizardFormData>();

    const {
        securityStep: { securityOption },
    } = useWatch({ control });

    // TODO Add conditional recommended badge to Secure

    return (
        <div>
            <h2>Security</h2>
            <p>Select the security option that best addresses your needs</p>
            <div className="mt-4">
                <h5 className="mb-1">
                    <Icon icon="lock" color="success" />
                    Secure
                </h5>
                <SetupWizardClickableCard
                    icon="lets-encrypt"
                    title="Generate Let’s Encrypt certificate"
                    description="Secure and hassle-free communication with automatic certificate management"
                    isSelected={securityOption === "letsEncrypt"}
                    onClick={() => setValue("securityStep.securityOption", "letsEncrypt")}
                />
                <SetupWizardClickableCard
                    className="mt-2"
                    icon="certificate"
                    title="Provide your own certificate"
                    description="Ideal for secure corporate setups with manual certificate management"
                    isSelected={securityOption === "ownCertificate"}
                    onClick={() => setValue("securityStep.securityOption", "ownCertificate")}
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

    const {
        securityStep: { securityOption },
    } = useWatch({ control });

    const handleBack = () => {
        setValue("currentStep", "License key");
    };

    const handleContinue = () => {
        switch (securityOption) {
            case "letsEncrypt":
                setValue("currentStep", "Self-signed certificate");
                break;
            case "ownCertificate":
                setValue("currentStep", "Domain");
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
            <div className="hstack gap-2">
                <FormCheckbox control={control} name="securityStep.isLetsEncryptAgreementAccepted">
                    I accept Let&apos;s Encrypt Subscriber Agreement
                </FormCheckbox>
                <Button variant="primary" className="rounded-pill" onClick={handleContinue}>
                    Continue <Icon icon="arrow-right" margin="m-0" />
                </Button>
            </div>
        </div>
    );
}
