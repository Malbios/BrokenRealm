class TrailTokenBehavior {
  static commands: CommandDefinition[] = [];

  describe(context: AnonymousBehaviorContext): VerbResult {
    return {
      effects: [{ type: "message", key: "token.describe", args: { label: context.this.properties.label } }]
    };
  }

  rename(context: AnonymousBehaviorContext): VerbResult {
    return {
      effects: [
        { type: "replaceValue", path: [...context.this.storagePath, "label"], value: context.args.label },
        { type: "message", key: "trail.renamed", args: { label: context.args.label } }
      ]
    };
  }
}

const anonymousBehaviorClasses = { TrailTokenBehavior };