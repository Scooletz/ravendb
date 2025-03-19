import { useFormContext, useWatch } from "react-hook-form";
import { SetupWizardFormData } from "../setupWizardValidation";
import { Icon } from "components/common/Icon";
import Button from "react-bootstrap/Button";
import messagePublisher from "common/messagePublisher";
import FileDropzone from "components/common/FileDropzone";
import { FormGroup, FormInput, FormLabel } from "components/common/Form";
import { useAsync, useAsyncCallback } from "react-async-hook";
import { useServices } from "components/hooks/useServices";
import { useAsyncDebounce } from "components/hooks/useAsyncDebounce";
import useBoolean from "components/hooks/useBoolean";
import Form from "react-bootstrap/Form";

export function SetupWizardSelfSignedCertificateStep() {
    const { control, setValue, clearErrors, setError } = useFormContext<SetupWizardFormData>();

    const { value: isFileProtected, setValue: setIsFileProtected } = useBoolean(false);

    const {
        selfSignedCertificateStep: { certificate, password, cns },
    } = useWatch({ control });

    const { setupWizardService } = useServices();

    const asyncGetCNs = useAsyncCallback(
        async () => {
            setValue("selfSignedCertificateStep.cns", []);

            if (!certificate) {
                return;
            }

            return setupWizardService.listHostsForCertificate(certificate, password);
        },
        {
            onSuccess: (cns) => {
                setValue("selfSignedCertificateStep.cns", Array.from(new Set(cns)));
                clearErrors("selfSignedCertificateStep.password");
                if (!password) {
                    setIsFileProtected(false);
                }
            },
            onError: (error) => {
                if ((error as unknown as JQueryXHR).status === 400) {
                    setIsFileProtected(true);
                    setError("selfSignedCertificateStep.password", { message: "Invalid password" });
                }
            },
        }
    );

    // Fetch CNs when certificate is changed immediately
    useAsync(asyncGetCNs.execute, [certificate]);

    // Fetch CNs when password is changed with debounce
    useAsyncDebounce(asyncGetCNs.execute, [password], 300);

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

    return (
        <div>
            <h2>Use self-signed certificate</h2>
            <p>For the highest security and control, you can use your own certificate once obtained.</p>
            <FormGroup>
                <FileDropzone onChange={handleFileChange} validExtensions={["pfx"]} maxFiles={1} />
            </FormGroup>
            {isFileProtected && (
                <FormGroup>
                    <FormLabel></FormLabel>
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
                    <FormLabel>CN Names</FormLabel>
                    <div className="vstack gap-2">
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
    const { setValue } = useFormContext<SetupWizardFormData>();

    const handleContinue = () => {
        setValue("currentStep", "Domain");
    };

    // TODO loading state

    return (
        <div className="hstack justify-content-end">
            <Button variant="primary" className="rounded-pill" onClick={handleContinue}>
                Continue <Icon icon="arrow-right" margin="m-0" />
            </Button>
        </div>
    );
}
