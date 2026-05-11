/**
 * Lightweight JSON Schema Validator
 * Validates JSON data against ConfigurationSchema.json
 */
(function() {
    'use strict';

    window.DashboardSchemaValidator = {
        /**
         * Validate data against schema
         * @param {object} data - JSON data to validate
         * @param {object} schema - JSON Schema
         * @returns {object} { valid: boolean, errors: array }
         */
        validate: function(data, schema) {
            var errors = [];
            this._validateObject(data, schema, '$', errors);
            return {
                valid: errors.length === 0,
                errors: errors
            };
        },

        /**
         * Recursively validate object
         */
        _validateObject: function(data, schema, path, errors) {
            if (!schema || !data) return;

            // Type validation
            if (schema.type) {
                var actualType = this._getType(data);
                var expectedType = schema.type;

                // Handle array of types (e.g., ["string", "null"])
                if (Array.isArray(expectedType)) {
                    if (expectedType.indexOf(actualType) === -1 && 
                        !(actualType === 'null' && expectedType.indexOf('null') !== -1)) {
                        errors.push({
                            path: path,
                            message: 'Expected type: ' + expectedType.join(' or ') + ', got: ' + actualType,
                            type: 'type_mismatch'
                        });
                        return;
                    }
                } else if (actualType !== expectedType && 
                           !(actualType === 'null' && expectedType === 'null')) {
                    // Special case: allow null for nullable types
                    if (expectedType !== 'null' || actualType !== 'null') {
                        errors.push({
                            path: path,
                            message: 'Expected type: ' + expectedType + ', got: ' + actualType,
                            type: 'type_mismatch'
                        });
                        return;
                    }
                }
            }

            // Enum validation
            if (schema.enum && data !== null && data !== undefined) {
                if (schema.enum.indexOf(data) === -1) {
                    errors.push({
                        path: path,
                        message: 'Value must be one of: ' + schema.enum.join(', '),
                        type: 'enum_invalid'
                    });
                }
            }

            // Pattern validation
            if (schema.pattern && typeof data === 'string') {
                var regex = new RegExp(schema.pattern);
                if (!regex.test(data)) {
                    errors.push({
                        path: path,
                        message: 'Value does not match pattern: ' + schema.pattern,
                        type: 'pattern_invalid'
                    });
                }
            }

            // Required fields validation
            if (schema.required && Array.isArray(schema.required)) {
                for (var i = 0; i < schema.required.length; i++) {
                    var requiredField = schema.required[i];
                    if (data[requiredField] === undefined || data[requiredField] === null) {
                        errors.push({
                            path: path + '.' + requiredField,
                            message: 'Required field is missing',
                            type: 'required_missing',
                            field: requiredField
                        });
                    }
                }
            }

            // Object properties validation
            if (schema.properties && typeof data === 'object' && !Array.isArray(data)) {
                for (var prop in schema.properties) {
                    if (data.hasOwnProperty(prop)) {
                        this._validateObject(
                            data[prop],
                            schema.properties[prop],
                            path + '.' + prop,
                            errors
                        );
                    }
                }
            }

            // Pattern properties validation (for dynamic keys)
            if (schema.patternProperties && typeof data === 'object' && !Array.isArray(data)) {
                for (var pattern in schema.patternProperties) {
                    var propSchema = schema.patternProperties[pattern];
                    var regex = new RegExp(pattern);
                    
                    for (var key in data) {
                        if (regex.test(key)) {
                            this._validateObject(
                                data[key],
                                propSchema,
                                path + '.' + key,
                                errors
                            );
                        }
                    }
                }
            }

            // Array items validation
            if (schema.items && Array.isArray(data)) {
                for (var i = 0; i < data.length; i++) {
                    this._validateObject(
                        data[i],
                        schema.items,
                        path + '[' + i + ']',
                        errors
                    );
                }
            }

            // Minimum/Maximum validation for numbers
            if (typeof data === 'number') {
                if (schema.minimum !== undefined && data < schema.minimum) {
                    errors.push({
                        path: path,
                        message: 'Value must be >= ' + schema.minimum,
                        type: 'minimum_violation'
                    });
                }
                if (schema.maximum !== undefined && data > schema.maximum) {
                    errors.push({
                        path: path,
                        message: 'Value must be <= ' + schema.maximum,
                        type: 'maximum_violation'
                    });
                }
            }

            // MinLength/MaxLength validation for strings
            if (typeof data === 'string') {
                if (schema.minLength !== undefined && data.length < schema.minLength) {
                    errors.push({
                        path: path,
                        message: 'String length must be >= ' + schema.minLength,
                        type: 'minLength_violation'
                    });
                }
                if (schema.maxLength !== undefined && data.length > schema.maxLength) {
                    errors.push({
                        path: path,
                        message: 'String length must be <= ' + schema.maxLength,
                        type: 'maxLength_violation'
                    });
                }
            }
        },

        /**
         * Get JavaScript type name
         */
        _getType: function(value) {
            if (value === null) return 'null';
            if (Array.isArray(value)) return 'array';
            return typeof value;
        }
    };
})();
