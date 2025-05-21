import { FormAceEditor, FormLabel, FormGroup, FormValidationMessage } from "components/common/Form";
import Row from "react-bootstrap/Row";
import Col from "react-bootstrap/Col";
import { useFormContext, useWatch } from "react-hook-form";
import { Icon } from "components/common/Icon";
import Button from "react-bootstrap/Button";
import Badge from "react-bootstrap/Badge";
import { ReactNode, useRef } from "react";
import IconName from "typings/server/icons";
import PopoverWithHoverWrapper from "components/common/PopoverWithHoverWrapper";
import AceEditor from "components/common/ace/AceEditor";
import ReactAce from "react-ace";

export default function EditGenAiTaskModelFields() {
    const {
        control,
        setValue,
        formState: { errors },
    } = useFormContext();

    const formValues = useWatch({ control });

    const promptRef = useRef<ReactAce>(null);
    const sampleObjectRef = useRef<ReactAce>(null);
    const jsonSchemaRef = useRef<ReactAce>(null);

    return (
        <>
            <FormGroup>
                <FormLabel>
                    Prompt
                    <PopoverWithHoverWrapper message="TODO">
                        <Icon icon="info" color="info" margin="ms-1" />
                    </PopoverWithHoverWrapper>
                </FormLabel>
                <FormAceEditor
                    aceRef={promptRef}
                    control={control}
                    name="prompt"
                    mode="text"
                    actions={[{ component: <AceEditor.FullScreenAction /> }]}
                />
            </FormGroup>
            {formValues.schemaProvider == null && (
                <div>
                    <div className="mb-1">
                        JSON schema
                        <PopoverWithHoverWrapper message="TODO">
                            <Icon icon="info" color="info" margin="ms-1" />
                        </PopoverWithHoverWrapper>
                    </div>
                    <Row>
                        <Col>
                            <SchemaProviderButton
                                icon="default"
                                title={
                                    <>
                                        Use sample object <Badge bg="faded-success">Recommended</Badge>
                                    </>
                                }
                                description="Choose if you want to generate schema out of sample object"
                                handleClick={() => setValue("schemaProvider", "sampleObject")}
                            />
                        </Col>
                        <Col>
                            <SchemaProviderButton
                                icon="edit"
                                title="Provide JSON Schema manually"
                                description="Choose if you want to manually provide the schema"
                                handleClick={() => setValue("schemaProvider", "jsonSchema")}
                            />
                        </Col>
                    </Row>
                    {errors.schemaProvider && (
                        <FormValidationMessage>{errors.schemaProvider?.message.toString()}</FormValidationMessage>
                    )}
                </div>
            )}
            {formValues.schemaProvider === "sampleObject" && (
                <FormGroup>
                    <FormLabel className="hstack justify-content-between">
                        <div>
                            Sample Object
                            <PopoverWithHoverWrapper message="TODO">
                                <Icon icon="info" color="info" margin="ms-1" />
                            </PopoverWithHoverWrapper>
                        </div>
                        <Button variant="link" size="xs" onClick={() => setValue("schemaProvider", "jsonSchema")}>
                            <Icon icon="edit" />
                            Provide JSON Schema manually
                        </Button>
                    </FormLabel>
                    <FormAceEditor
                        aceRef={sampleObjectRef}
                        control={control}
                        name="sampleObject"
                        mode="json"
                        actions={[
                            { component: <AceEditor.FullScreenAction /> },
                            { component: <AceEditor.FormatAction /> },
                            {
                                component: (
                                    <AceEditor.LoadFileAction
                                        onLoad={(value) => setValue("sampleObject", value, { shouldValidate: true })}
                                    />
                                ),
                            },
                        ]}
                    />
                </FormGroup>
            )}
            {formValues.schemaProvider === "jsonSchema" && (
                <FormGroup>
                    <FormLabel className="hstack justify-content-between">
                        <div>
                            JSON Schema
                            <PopoverWithHoverWrapper message="TODO">
                                <Icon icon="info" color="info" margin="ms-1" />
                            </PopoverWithHoverWrapper>
                        </div>
                        <Button variant="link" size="xs" onClick={() => setValue("schemaProvider", "sampleObject")}>
                            <Icon icon="default" />
                            Use sample object
                        </Button>
                    </FormLabel>
                    <FormAceEditor
                        aceRef={jsonSchemaRef}
                        control={control}
                        name="jsonSchema"
                        mode="json"
                        actions={[
                            { component: <AceEditor.FullScreenAction /> },
                            { component: <AceEditor.FormatAction /> },
                            {
                                component: (
                                    <AceEditor.LoadFileAction
                                        onLoad={(value) => setValue("jsonSchema", value, { shouldValidate: true })}
                                    />
                                ),
                            },
                        ]}
                    />
                </FormGroup>
            )}
        </>
    );
}

interface SchemaProviderButtonProps {
    icon: IconName;
    title: ReactNode;
    description: ReactNode;
    handleClick: () => void;
}

function SchemaProviderButton({ icon, title, description, handleClick }: SchemaProviderButtonProps) {
    return (
        <div className="border border-secondary rounded p-2 cursor-pointer h-100" onClick={handleClick}>
            <div className="text-emphasis hstack gap-2 h-100">
                <div>
                    <Icon icon={icon} margin="m-0" style={{ fontSize: 24 }} />
                </div>
                <div className="flex-grow">
                    <h4 className="mb-1">{title}</h4>
                    <span>{description}</span>
                </div>
            </div>
        </div>
    );
}
