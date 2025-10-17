import { useFormContext, useWatch } from "react-hook-form";
import { SetupWizardFormData } from "../setupWizardValidation";
import { Icon } from "components/common/Icon";
import Button from "react-bootstrap/Button";
import messagePublisher from "common/messagePublisher";
import FileDropzone from "components/common/FileDropzone";
import { FormGroup, FormInput, FormLabel } from "components/common/Form";
import { useServices } from "components/hooks/useServices";
import useBoolean from "components/hooks/useBoolean";
import Form from "react-bootstrap/Form";
import PopoverWithHoverWrapper from "components/common/PopoverWithHoverWrapper";
import React from "react";
import { useAsyncDebounce } from "hooks/useAsyncDebounce";

export function SetupWizardSelfSignedCertificateStep() {
    const { control, setValue, clearErrors, setError } = useFormContext<SetupWizardFormData>();

    const { value: isFileProtected, setValue: setIsFileProtected } = useBoolean(false);

    const {
        selfSignedCertificateStep: { certificate, password, cns },
    } = useWatch({ control });

    const { setupWizardService } = useServices();

    const asyncGetCNs = useAsyncDebounce(
        async () => {
            try {
                setValue("selfSignedCertificateStep.cns", []);

                if (!certificate) {
                    return;
                }

                return setupWizardService.listHostsForCertificate(certificate, password);
            } catch (e) {
            } finally {
            }
        },
        [password, certificate],
        300,
        {
            onSuccess: (cns) => {
                const uniqueCns = Array.from(new Set(cns));
                setValue("selfSignedCertificateStep.cns", uniqueCns);

                const isWildcardCertificate = uniqueCns.some((cn) => cn.startsWith("*"));
                setValue("selfSignedCertificateStep.isWildcardCertificate", isWildcardCertificate);

                clearErrors("selfSignedCertificateStep.password");

                if (!password) {
                    setIsFileProtected(false);
                }
            },
            onError: (error) => {
                if ((error as unknown as JQueryXHR).status === 400) {
                    setIsFileProtected(true);

                    if (password.length > 0) {
                        setError("selfSignedCertificateStep.password", { message: "Invalid password" });
                    }
                }
            },
        }
    );

    const handleFileChange = (files: File[]) => {
        const file = files[0];
        const fileName = file.name;
        const reader = new FileReader();

        reader.onload = function () {
            const textResult = String(reader.result);

            const isFileSelected = fileName ? !!fileName.trim() : false;

            if (!isFileSelected) {
                clearFile();
                messagePublisher.reportError("Failed to load file");
                return;
            }

            setValue("selfSignedCertificateStep.certificateFileName", fileName.split(/(\\|\/)/g).pop());

            // dataUrl has following format: data:;base64,PD94bW... trim on first comma
            setValue("selfSignedCertificateStep.certificate", textResult.substring(textResult.indexOf(",") + 1));
        };

        reader.onerror = function () {
            clearFile();
            messagePublisher.reportError("Failed to load file", reader.error.message);
        };

        reader.readAsDataURL(file);
    };

    const clearFile = () => {
        setValue("selfSignedCertificateStep.certificate", "");
    };

    console.log("maxym asyncGetCns", asyncGetCNs);

    return (
        <div>
            <h2 className="mb-1">Use self-signed certificate</h2>
            <p className="mb-4 text-muted">
                For the highest security and control, you can use your own certificate once obtained.
            </p>
            <FormGroup>
                <FileDropzone onChange={handleFileChange} validExtensions={["pfx"]} maxFiles={1} />
            </FormGroup>
            {isFileProtected && (
                <FormGroup>
                    <FormLabel className="hstack justify-content-between">
                        Passphrase
                        <PopoverWithHoverWrapper
                            message={
                                <>
                                    <p className="mb-0">
                                        Enter the passphrase used to encrypt your private key. This is required to
                                        unlock and use your certificate.
                                    </p>
                                    <hr className="my-2" />
                                    <span className="md-label">
                                        <Icon icon="link" /> Read more in our{" "}
                                        <a href="#TODO" target="_blank">
                                            documentation <Icon icon="newtab" />
                                        </a>
                                    </span>
                                </>
                            }
                        >
                            <div className="text-info">
                                <Icon icon="info" size="xs" /> What is this?
                            </div>
                        </PopoverWithHoverWrapper>
                    </FormLabel>
                    <FormInput
                        type="password"
                        control={control}
                        name="selfSignedCertificateStep.password"
                        placeholder="Enter your passphrase"
                    />
                </FormGroup>
            )}
            {cns.length > 0 && (
                <FormGroup>
                    <FormLabel className="hstack">
                        CN Names
                        <PopoverWithHoverWrapper
                            message={
                                <>
                                    <p className="mb-0">
                                        The Common Name (CN) is automatically extracted from your certificate. It
                                        represents the domain or hostname the certificate is issued for.
                                    </p>
                                    <hr className="my-2" />
                                    <span className="md-label">
                                        <Icon icon="link" /> Read more in our{" "}
                                        <a href="#TODO" target="_blank">
                                            documentation <Icon icon="newtab" />
                                        </a>
                                    </span>
                                </>
                            }
                        >
                            <Icon icon="info-new" />
                        </PopoverWithHoverWrapper>
                    </FormLabel>
                    <div className="vstack gap-1">
                        {cns.map((cn) => (
                            <div className="panel-bg-2">
                                <Form.Control key={cn} type="text" value={cn} disabled readOnly />
                            </div>
                        ))}
                    </div>
                </FormGroup>
            )}
        </div>
    );
}

export function SetupWizardSelfSignedCertificateStepFooter() {
    const { setValue, control } = useFormContext<SetupWizardFormData>();
    const { setupWizardService } = useServices();

    const {
        selfSignedCertificateStep: { certificate, password },
    } = useWatch({ control });

    const handleContinue = () => {
        setValue("currentStep", "Domain");
    };

    const handleBack = () => {
        setValue("currentStep", "Security");
    };

    return (
        <div className="hstack justify-content-between">
            <Button variant="secondary" className="rounded-pill" onClick={handleBack}>
                <Icon icon="arrow-left" /> Back
            </Button>
            <Button
                variant="primary"
                className="rounded-pill"
                disabled={!certificate}
                onClick={handleContinue}
                // isSpinning={asyncGetCNs.loading}
            >
                Continue&nbsp;
                <Icon icon="arrow-right" margin="m-0" />
            </Button>
        </div>
    );
}
