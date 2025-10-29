grammar ndf;

// -------------------- Parser Rules --------------------

file: declaration+ EOF;

declaration
    : EXPORT? ID IS value #assignDecl
    | UNNAMED value #unnamedDecl
    ;

value
    : NUMBER #numericLiteral
    | STRING #stringLiteral
    | GUID_LITERAL #guidLiteral
    | array #arrayValue
    | ID '(' (ID '=' value)* ')' #objectValue
    | ID array #structValue
    | '(' value ',' value ')' #pairValue
    | value '|' value #orValue
    | PATH #pathValue
    | ID #idValue
    | ID IS value #assignValue
    | NIL #nilLiteral
    | RELREFERENCE #refRelativeValue
    | ABSREFERENCE #refAbsoluteValue
    ;

array
    : '[' ']'
    | '[' (value ',')* value ','? ']';

// -------------------- Lexer Rules --------------------

GUID_LITERAL: 'GUID:{' [0-9a-fA-F-]+ '}';

NIL: 'nil';
EXPORT: 'export';
IS: 'is';
UNNAMED: 'unnamed';

ID : [a-zA-Z_][a-zA-Z_0-9]*;

RELREFERENCE: '~'('/'[A-Za-z_][a-zA-Z_0-9]*)+ ;

ABSREFERENCE: '$'('/'[A-Z_][a-zA-Z_0-9]*)*'/'[A-Za-z_][a-zA-Z_0-9]* ;

PATH: [A-Z_][a-zA-Z_0-9]*('/'[A-Z_][a-zA-Z_0-9]*)+;

NUMBER : '-'? [0-9]+ ('.' [0-9]+)? ;

STRING
    : '\'' (~['\r\n])* '\''
    | '"' (~["\r\n])* '"'
    ;

LINE_COMMENT : '//' ~[\r\n]* -> skip ;


WS: [ \t\n\r]+ -> channel(HIDDEN);